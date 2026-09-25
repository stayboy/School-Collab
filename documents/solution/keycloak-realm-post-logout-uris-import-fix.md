# Keycloak realm import — the post-logout allowlist must be a client **attribute**

> Fixes a defect shipped by round `keycloak-logout-oidc` (pass 3b).
> Related: `documents/configuration.md` § *Round B — the post-logout landing URI*,
> `documents/runbooks/aspire-apphost-silent-hang.md`,
> `documents/specs/keycloak-ui-auth-integration.md` (D13 option (ii), D16).

## Finding

### Symptom

`aspire run` (or `dotnet run --project src/AppHost/SchoolCollab.AppHost`) reached the Aspire
dashboard — so the AppHost itself looked healthy — but the topology never came up. Keycloak was
never healthy, `auth` therefore never started, `auth-portal` never started behind it, and the
pinned portal host port surfaced as a **proxied endpoint with no target**: a request to
`http://localhost:5700/health` hung until the client timed out instead of failing fast.

```text
$ docker ps -a --format '{{.Names}}|{{.Status}}'
keycloak-zwcgfbjz|Exited (1) 3 minutes ago|quay.io/keycloak/keycloak:26.4.7
```

### Root cause

```text
ERROR [org.keycloak.quarkus.runtime.cli.ExecutionExceptionHandler] Failed to run import
ERROR: Unrecognized field "postLogoutRedirectUris"
  (class org.keycloak.representations.idm.ClientRepresentation), not marked as ignorable
  (44 known properties: "enabled", …, "redirectUris", …, "attributes", …, "webOrigins", …)
  at RealmRepresentation["clients"]->ArrayList[0]->ClientRepresentation["postLogoutRedirectUris"]
```

`school-collab-realm.json` registered the post-logout allowlist as a **top-level client field**.
Keycloak 26.4.7's `ClientRepresentation` — the type the realm importer deserializes — declares no
such property, and its reader is strict, so `--import-realm` aborts the **whole realm** and the
container exits `1`.

The blast radius is the `WaitFor` chain: `auth` `.WaitFor(keycloak)`, `auth-portal` `.WaitFor(auth)`.
One rejected field therefore silently costs the entire dev topology, while the AppHost log shows
nothing worse than a resource that never becomes healthy.

### Why no test caught it

Every hermetic guard reads the realm file as **text/JSON**: the strict-JSON check, the redirect-URI
guards, and — before this change — an allowlist guard that asserted *this very field*. CI never
starts Keycloak and never calls `Run()`, so a shape that no Keycloak will import is
indistinguishable from a correct one. The sibling `targetPort` crash fixed on the same branch hides
in exactly the same blind spot.

### The accepted shape — proven, not inferred

`attributes["post.logout.redirect.uris"]`, URIs separated by `##`. Two Keycloak-class facts:

- `unzip`/`javap` are **not** in the `quay.io/keycloak/keycloak` image, so inspecting
  `ClientRepresentation` in place is a dead end — copy the jar out, or probe an import.
- `docker cp` into a **created-but-stopped** container fails silently when the destination
  directory does not exist. The import then never runs and Keycloak starts **without any error** —
  a convincing false positive that cost one wrong intermediate conclusion here. Mount the file (as
  the AppHost does, via `WithBindMount`) and require the log line `Realm 'school-collab' imported`.

## Implementation

| File | Change |
| :--- | :--- |
| `src/AppHost/SchoolCollab.AppHost/school-collab-realm.json` | the five URIs move into `school-collab-client` → `attributes["post.logout.redirect.uris"]` (`##`-separated); the rejected top-level array is gone |
| `tests/SchoolCollab.ArchitectureTests.Unit/AppHostRealmImportArchitectureTests.cs` | the allowlist guard reads the attribute (split on `##`) and gains a **negative** assertion that the top-level field is absent; `PinnedAuthPortalHostPort` now strips line comments before slicing at the statement's `;` — a `;` inside a comment between the resource creation and the pin used to make that fail-loud helper fail for the *wrong* reason |
| `documents/configuration.md` | the post-logout section documents the attribute shape and why the top-level field is fatal; the "effective runtime URL" note is replaced by what is now **observed** |
| `src/AppHost/SchoolCollab.AppHost/Program.cs` | the pin's rationale comment names the attribute instead of the removed field |

## Verification

| Check | Result |
| :--- | :--- |
| `dotnet build SchoolCollab.slnx` | **0 errors** |
| `dotnet test tests/SchoolCollab.ArchitectureTests.Unit` | **66 / 0** |
| Live import of the committed realm (Keycloak 26.4.7, bind-mounted) | `Realm 'school-collab' imported` · `Import finished successfully` · `started in 9.213s` |
| Live RP-initiated logout, **unlisted** `post_logout_redirect_uri` (control) | `HTTP 400 Bad Request` |
| Live RP-initiated logout, **registered** `http://localhost:5700/` | `HTTP 302 Found` · `Location: http://localhost:5700/` |
| Guard discrimination — top-level field restored | guard **fails** (as designed) |
| Guard discrimination — attribute key renamed | guard **fails** (as designed) |
| AppHost `Run()` completes · host port `5700` listening | observed by manual smoke run (no hermetic equivalent) |

The control/positive pair is what makes the logout result meaningful: the identical request with an
unlisted URI is **rejected**, so the `302` is Keycloak matching the committed allowlist — including
the `##` separator semantics — rather than redirecting anything it is handed.

## Residual

- No CI check starts Keycloak or the AppHost, so this class (a config shape that only a runtime
  component rejects) is guarded **statically** — shape pins — rather than dynamically. A
  Docker-backed realm-import integration test is the stronger option if its CI cost is acceptable.
- `##` multi-value semantics are proven for the **logout match path only** (the probe above); other
  multi-valued client attributes were not exercised.
