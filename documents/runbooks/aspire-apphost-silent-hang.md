# Runbook — Aspire AppHost hangs forever at startup with no error

> Owner: Dev environment / tooling
> Related: `documents/configuration.md` §1–§2 (AppHost topology),
> upstream [microsoft/aspire#18922](https://github.com/microsoft/aspire/issues/18922)
> Last updated: 2026-09-25

## Symptom

`dotnet run --project src/AppHost/SchoolCollab.AppHost` (or `aspire run`) never
reaches the dashboard. The log ends with a block of **exactly one warning per
network endpoint** and nothing after it:

```text
warn: Unable to allocate a network port for service 'postgres'
warn: Unable to allocate a network port for service 'keycloak-http'
warn: Unable to allocate a network port for service 'cache-tcp'
... 8 lines total, including resources you did not touch ...
```

Distinguishing characteristics:

- **No `fail:` or `crit:` lines at all** — the log looks benign.
- `dcp.exe` (the DCP API server) **is** running; the AppHost **is** connected.
- Every resource is created and sits in `Starting -> Waiting` forever.
- **No containers are ever created** (not even the dashboard).
- The dashboard never listens on `:15006`.
- The log is **byte-identical across runs**, and the stall survives restarts,
  different launch contexts, and unrelated changes.

## Root cause

`dcp run-controllers` — a **separate** DCP child process from `dcp start-apiserver`
— exits immediately with status 1:

```json
{"ExitCode":1,"error":"failed to initialize state store: could not prepare state store
 directory 'C:\\Users\\<user>\\.dcp\\state.elevated': directory ... has invalid ownership:
 directory owner does not match current user or token owner"}
```

The API server keeps running, so the AppHost connects, builds its resource model,
resolves parameters, creates the endpoint Services — and then **nothing reconciles
them, ever**. DCP has no timeout for "no controller is processing this", so the
process waits forever and prints nothing.

The `1-minute` service-address watch inside that reconciliation path is what
produces the misleading `Unable to allocate a network port` warnings.

Sticky: once `~/.dcp/state` or `~/.dcp/state.elevated` reaches this state, **every**
AppHost run on the machine hangs until the directory is cleared. A stale directory
from a previous DCP/Aspire version (version skew between the Aspire CLI bundle and
`Aspire.Hosting` migrates the shared store) is the usual trigger.

## Diagnosis (2 minutes)

1. **Count DCP processes.** A healthy run has a controller process alongside the
   API server; the broken state has only one:

   ```powershell
   tasklist | Select-String dcp
   ```

2. **Run the controller by hand — it prints the real error in under a second.**
   Point it at the newest DCP session directory and the running AppHost PID:

   ```bash
   DCP="$USERPROFILE/.nuget/packages/aspire.hosting.orchestration.win-x64/<version>/tools/dcp.exe"
   K=$(ls -dt "$TEMP"/aspire-dcp* | head -1)
   "$DCP" run-controllers --kubeconfig "$K/kubeconfig" --monitor <apphost-pid> \
       --container-runtime docker -v=debug
   ```

   The `failed to initialize state store ... invalid ownership` line confirms it.

3. *Optional* — set `DcpPublisher__DiagnosticsLogFolder=<dir>` when launching the
   AppHost to have the same error written to file.

## Fix

Rename (safer than deleting) the DCP state directories, then relaunch. DCP
recreates them with correct ownership:

```powershell
cd $env:USERPROFILE\.dcp
Rename-Item state.elevated state.elevated.bak
Rename-Item state state.bak
```

Verify after relaunch: **two or more `dcp.exe` processes**, **zero**
`Unable to allocate a network port` warnings, and containers actually appearing
(`docker ps`). The stale `*.bak` directories can be deleted once the AppHost is up.

## Red herrings (each of these was tested and does **not** fix it)

| Hypothesis | Why it looks plausible | Reality |
|---|---|---|
| Port exhaustion | the warning literally says "unable to allocate a port" | ~16 000 dynamic ports available, only a few listeners; no ports in use in the warned ranges |
| Windows excluded/reserved port ranges | `netsh int ipv4 show excludedportrange` shows reserved blocks | the reserved ranges do not overlap the ports DCP wants |
| Stale containers holding ports | postgres/rabbitmq containers linger between runs | stopping them changes nothing |
| Missing `Parameters:` values | the AppHost prompts/waits for unset parameters | supplying them lets the parameter resources reach `Running`, but the stall persists |
| `DOCKER_HOST` / named-pipe choice | Docker Desktop context differences | setting it explicitly changes nothing |
| Docker Desktop health | plausible single point of failure | engine healthy, `docker network create` returns instantly |
| Aspire version mismatch | CLI bundle and `Aspire.Hosting` versions differ | not the trigger, though it *is* what corrupts the state store in the first place |

## Prevention

- Keep the Aspire CLI bundle and `Aspire.Hosting` on the same version
  (`aspire update`) — the shared `~/.dcp/state` store is migrated by whichever
  DCP touches it first, and a version skew is what produced the stale directory.
- If the AppHost ever hangs silently again, **check the DCP controller process
  count first** — it takes seconds and skips every red herring above.

## Related failure mode (same silent-hang family)

A host that reports **Healthy** while logging a connection error on every retry
(e.g. `Npgsql ... Failed to connect to 127.0.0.1:5432`) is a *missing
`.WithReference`* problem, not a DCP problem. See
`documents/configuration.md` §8 and `AppHostSettingsDbWiringArchitectureTests`.

## Related failure mode: a dependency container exited `1`

If the dashboard **does** come up and containers **are** created, but a resource never becomes
healthy, stop reading the AppHost log — it stays quiet — and look for a crashed dependency:

```powershell
docker ps -a --format '{{.Names}}|{{.Status}}'      # e.g. keycloak-xxxx|Exited (1) 3 minutes ago
docker logs <that-container> 2>&1 | Select-String 'ERROR'
```

Seen 2026-09-25: `keycloak` exited `1` because the realm import rejected an unknown field
(`Unrecognized field "postLogoutRedirectUris"`). Because `auth` `.WaitFor(keycloak)` and
`auth-portal` `.WaitFor(auth)`, that one rejection silently blocked the whole chain — and the
portal's **pinned** host port `5700` then listened as a proxied endpoint *with no target*, so
requests **hung** rather than being refused (a refused connection would have been far easier to
diagnose). Diagnosis, fix and the accepted realm shape:
`documents/solution/keycloak-realm-post-logout-uris-import-fix.md`.
