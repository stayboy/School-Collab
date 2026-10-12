# Blazor agent skills — adoption of the `dotnet/skills` → `dotnet-blazor` plugin

**Status:** implemented (2026-10-09) · **Scope:** agent tooling (user-level skills), not product code
**Source:** [`dotnet/skills`](https://github.com/dotnet/skills) → `plugins/dotnet-blazor` (`plugin.json` v0.1.2, MIT)

## 1. Findings

### 1.1 What the plugin contains

Ten skills covering the Blazor **framework** surface: `author-component`,
`collect-user-input`, `configure-auth`, `coordinate-components`,
`create-blazor-project`, `fetch-and-send-data`, `plan-ui-change`,
`support-prerendering`, `use-igniteui-blazor`, `use-js-interop`.

### 1.2 Nine of the ten were already present — as unprovenanced copies

Nine were sitting in `~/.pi/agent/pi-hermes-memory/skills/` (Pi-global, visible to every
session), captured 2026-07-16 as *upstream file + a prepended Pi frontmatter block*. None
carried a `source:` field and none appeared in `~/.agents/.skill-lock.json` — so they sat
outside the sanctioned `refresh-vendored-agent-skill` path despite being vendored copies.

| Defect | Detail |
|---|---|
| Unprovenanced | No `source:` frontmatter, no installer lock entry → cannot be verified or refreshed the sanctioned way. |
| Double frontmatter | All nine files carried **two** frontmatter blocks (Pi's, then upstream's verbatim); loaders reading only the first treat the second as body text. |
| Content gap | All nine were `SKILL.md`-only. Upstream ships `references/` for two of them (`author-component` 3 files, `create-blazor-project` 7 files) — the local copies never had them. |
| Duplicated rule | `collect-user-input` gained a local 2.2 KB section *"Share form fields between create & edit forms"* — a copy of `.github/copilot/rules/blazor-components.md` § *"Share form fields between create & edit forms"*. One rule, two lifecycles. |
| Local rewrite | `use-js-interop` had its JS sample locally rewritten (`access-token` → `client-id`) — exactly the hand-patch the vendored-skill rule forbids; it made the snapshot unverifiable. |
| Cosmetic drift | `configure-auth` had two literal `<NotAuthorized>` tags stripped to `NotAuthorized`, and one table row lost its trailing `|`. |

### 1.3 Nothing to import — the value was hygiene

Six of the nine bodies were byte-identical to upstream `main`; the other three differed only
via the local edits above. No upstream drift existed to pull in.

### 1.4 Alternatives considered

- **(i) Hand-add `source:` to each file.** Records provenance, but refresh stays manual and
  the files remain duplicated in a non-canonical directory.
- **(ii) Re-fetch through the installer** (`npx skills`, vercel-labs/skills) so
  `~/.agents/.skill-lock.json` owns them — canonical copy in `~/.agents/skills/`, agent dirs
  symlinked in, real `skillFolderHash` integrity check. **Adopted.**
- **(iii) Leave as-is.** Rejected: keeps five defects and an unreproducible snapshot.
- **`use-igniteui-blazor`:** **rejected** — Ignite UI for Blazor Lite is a second component
  library; this repo standardises on `Microsoft.FluentUI.AspNetCore.Components`. Adopting its
  setup guide would cost discovery budget on every Blazor task for no coverage gain.

### 1.5 Installer facts worth knowing

- `npx skills add <repo> -s <skill>` takes **one skill per `-s` flag**; repeated flags work,
  a comma- or space-separated list does not (the whole string is treated as one skill name).
- `-l/--list` lists a repo's skills **without installing** — use it before `add`; `dotnet/skills`
  exposes **106** skills across its 16 plugins, so a bare `add` would be a large unintended install.
- `add` reports `"status": "failed"` / `"PromptScript does not support global skill installation"`
  for every skill even when the install **fully succeeds** (files written, lock entry written,
  symlinks created). Treat the filesystem + lock as authoritative, not that status string.
- `npx skills check` is **not** a read-only command — it applies updates. Use `list` to inspect.

## 2. Decisions (settled 2026-10-09)

1. **Scope:** provenance + hygiene only — no editorial content pass.
2. **Provenance mechanism:** installer-managed (canonical `~/.agents/skills/` + lock), not hand-added frontmatter.
3. **`use-igniteui-blazor`:** not adopted.
4. **Discovery:** record the precedence rule in `AGENTS.md` and this document in `documents/solution/`.

## 3. Implementation

```bash
# 2 passes: repeated -s flags, one skill per flag
npx --yes skills add dotnet/skills -g -y -s author-component -s plan-ui-change -s use-js-interop
npx --yes skills add dotnet/skills -g -y -s collect-user-input -s configure-auth \
  -s coordinate-components -s create-blazor-project -s fetch-and-send-data -s support-prerendering
```

- The nine unprovenanced copies were removed from
  `~/.pi/agent/pi-hermes-memory/skills/` (quarantined first at
  `%TEMP%\hermes-blazor-quarantine\` — see Open items).
- `AGENTS.md`: one positive-selection row pointing here, plus a *"Generic Blazor skills vs.
  repo rules"* paragraph establishing that **repo rules win** and naming the two skills that
  do not describe this repo's stack.

## 4. Verification

Pinned clone of `dotnet/skills` `main` @ `3d38ac34`; for each skill, every file compared
byte-for-byte against the clone and the lock's `skillFolderHash` compared against
`git rev-parse <HEAD>:plugins/dotnet-blazor/skills/<skill>`.

| Skill | Files | Bytes vs clone | `skillFolderHash` = tree hash |
|---|---|---|---|
| author-component | 4 | identical | PASS |
| create-blazor-project | 8 | identical | PASS |
| collect-user-input, configure-auth, coordinate-components, fetch-and-send-data, plan-ui-change, support-prerendering, use-js-interop | 1 each | identical | PASS |

Also confirmed: lock entry count 49 → 58; canonical copy present for all nine under
`~/.agents/skills/`; `~/.claude/skills/<skill>` symlinks resolve into it; no duplicate copy
remains in any other agent directory.

## 5. Open items

- **`use-js-interop` sample came back upstream-verbatim.** The refresh restored upstream's
  `sessionStorage.setItem('access-token', …)` illustration, discarding the local `client-id`
  scrub. Re-patching the installed file is not an option (vendored rule), so if the
  access-token illustration is worth suppressing it belongs in a **repo-owned** rule — not in
  the vendored skill.
- **Quarantine folder** `%TEMP%\hermes-blazor-quarantine\` holds the nine superseded copies;
  delete once a session has confirmed the installer-managed skills load correctly.
- **Adjacent plugin candidates, not yet evaluated:** `dotnet-test` (21 skills — `filter-syntax`,
  `mtp-hot-reload`, `platform-detection`, `writing-mstest-tests`; relevant because
  `testing.md` is "MTP Standard"), `dotnet-data` (`optimizing-ef-core-queries`),
  `dotnet-aspnetcore` (`dotnet-webapi`). Worth a separate exploration.
