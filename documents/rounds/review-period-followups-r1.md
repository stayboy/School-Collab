## Review

### Correct (with evidence)
- **§1 stale-path fix** — `git grep -c "documents/specs/" -- "documents/rounds/*-drop-periodtype.md"` returned 0 matches. All four round docs now reference `documents/rounds/` internally, and `acceptance-drop-periodtype.md` §10 reads: *"Round docs were moved to `documents/rounds/`."* while keeping the historical note.
- **§4 `PeriodForm.razor` P2-5/P2-6 fix matches the plan** (`src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodForm.razor`):
  - Blocked state is set only for `!PeriodId.HasValue && InitialParentPeriodId.HasValue` when the resolved parent division is `"None"`, including the parent-not-found fallback (`parent?.Division ?? "None"`) — line 242-246.
  - When blocked, the editable form fields (Division select, parent select, name + suggest/backfill, dates, submit row) are **replaced** by a `FluentMessageBar` warning + a "Back to periods" `FluentButton` — lines 25-29.
  - `CancelAsync` (lines 271-277) honors `OnCancel` before falling back to `CancelRoute`; no hardcoded route in the component.
  - Prefill gate is explicit (`!_parentBlocked`) with a documenting comment — line 261-263.
  - Edit mode, `ShowHeader`/`AutoActivateOnCreate` wizard behavior, and `SubmitAsync` are untouched.
  - No P2-3/4/7/8 code was added to `PeriodForm.razor`.
- **Test additions are clean** (`tests/SchoolCollab.Students.Tests.Unit/SchoolCollab.Students.Tests.Unit.csproj`):
  - `bunit` reference has no `Version` attribute (CPM-compliant); `Directory.Packages.props` already pins it at 2.7.2.
  - `ProjectReference` to `SchoolCollab.Students.Application` is correct.
  - New `PeriodFormBlockedParentTests.cs` has four test cases that assert exactly what they claim (blocked render, working back affordance, no prefill, positive control), uses `JsonSerializerOptions(JsonSerializerDefaults.Web)` matching `StudentsApiClient.ListPeriodsAsync`'s `ReadFromJsonAsync` defaults, and uses a scripted `HttpMessageHandler` (no real networking, no flaky timers).
- **Build/test matrix green**:
  - `dotnet build SchoolCollab.sln -c Debug --nologo -v q` → 0 errors, 6 warnings (pre-existing).
  - `dotnet test tests/SchoolCollab.Students.Tests.Unit` → 364/0 (360 baseline + 4 new).
  - `dotnet test tests/SchoolCollab.Admin.Tests.Unit` → 502/0.
  - `dotnet test tests/SchoolCollab.Settings.Tests.Unit` → 446/0.

### Finding
- **P2 — out-of-round scope deviation**: `.pi/skills/orchestrator-worker-reviewer/SKILL.md` is modified (+61 lines). Neither `plan-period-followups-r1.md` nor the worker task authorized editing the orchestrator skill file. It is a non-destructive, additive doc-only edit, but it is a scope deviation that the parent should revert or quarantine before the working tree is committed.
- **P2 — cosmetic spacing**: `PeriodForm.razor` lines 28-29 place the blocked-panel "Back to periods" button directly after the `FluentMessageBar` with no spacing wrapper; consistent with existing markup would mean a small `mt-3`/gap wrapper. Functionally harmless.

### Merge verdict
OK with notes — no P0/P1 blockers. The only action before commit is to decide what to do with the unauthorized `.pi/skills/orchestrator-worker-reviewer/SKILL.md` edit.