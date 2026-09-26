# `FEATURE:EnableActivityGroups` — client-side isolation of a dark-launch flag

> **Status (2026-09-26):** landed and covered by bUnit tests. Read this before
> touching any surface that calls `ListActivityGroupsAsync`.
>
> **Folder:** `solution/` — findings + decision + implementation record. The
> governing requirements live in `specs/activity-group-enrollment.md` (NFR-11 is
> the dark-launch clause). Related: `subject-topic-delivery-periods.md` shares
> several of the pitfalls below.

---

## 1. The root cause in one line

`/activity-groups` is **not mapped at all** when the flag is off, so every
unguarded client call **404s** — and in three of the four surfaces that call sat
in a `try` block that also loaded something the user actually needs.

```csharp
// SchoolCollab.Students.Api/StudentEndpoints.cs
if (featureFlags.IsEnabled(FeatureFlagKeys.EnableActivityGroups))
{
    var activityGroupsGroup = app.MapGroup("");
    activityGroupsGroup.MapActivityGroupRoutes();   // absent entirely when OFF
}
```

The flag ships **dark** (`false`) for every tenant except the pilot
(`Hydeson School`), so this was the *default* experience for nearly all
tenants — not an edge case.

---

## 2. Findings

### Finding 1 — an optional resource could take down a required one

`Assignments/Create.razor` loaded grade levels **and** activity groups inside a
single `try`. The grade-level `catch` only logged and continued, so a 404 from
the unmapped route left the wizard with **no grade options at all** — while the
UI still offered a "Target specific grade levels" card. The subject picker is
the core of the wizard; it silently vanished.

Same shape on the Subjects landing: one `catch` set `_error`, but `Loading` is
bound to `_items is null`, so a failure left the grid **spinning forever**
instead of showing the error. (`Error="@_error"` was not even bound on the
grid — the message had nowhere to render.)

### Finding 2 — the flag gates the API but the UI offered gated choices anyway

With the flag off, the Subjects landing and the Topics create dialog still
rendered an "Activity Group" option. Selecting it produced either an empty
picker or a raw 404 — an affordance for a feature the tenant cannot have.

### Finding 3 — a pre-seeded group owner had nowhere to fall back to

`TopicCreateDialog` is opened pre-seeded with `OwnerType = "ActivityGroup"` by
the Subjects landing. With the flag off that owner is unselectable, so the
dialog rendered a picker with no options and a subject that could not be
created. It now falls back to `GradeLevel` — the owner that is always
available.

### Finding 4 — a 404 is indistinguishable from a real outage

Because the route is *unmapped* rather than *rejected*, the client gets a 404
rather than a 403-with-a-reason. Status code alone cannot tell "flag off" from
"endpoint broken", so client code must consult `IFeatureFlagService` before
calling. This is why the fix is flag-guarded rather than exception-sniffed.

---

## 3. Decision

**Rule: a flag-gated resource is loaded last, in its own guarded method, and
must never share a `try` block with a required resource. A flag-off state
degrades a control out of the UI — it is not an error.**

Applied consistently to all four surfaces:

| Surface | Change |
|---|---|
| `Subjects.razor` | Groups load in a separate `LoadActivityGroupsAsync`; the "Activity Group" owner option is gated; `Error="@_error"` bound; failure sets `_items = []` so `Loading` clears |
| `Assignments/Create.razor` | `SelectedGroups` card wrapped in `FeatureFlagGate`; groups load last in their own method, flag-checked first, isolated from the grade-level `try` |
| `TopicCreateDialog.razor` | `_activityGroupsEnabled` resolved first; groups fetched only when enabled; pre-seeded group owner falls back to `GradeLevel` |
| `JoinGroupsDialog.razor` | Defense-in-depth flag check before the call, yielding a readable message instead of a raw 404; also fixed `Error="Error"` → `Error="@Error"` |

Note `JoinGroupsDialog`'s gate is genuinely defense-in-depth: its only caller,
`Students/Detail.razor`, **is** already gated (line ~179). It is kept because
the flag can flip between opening the dialog and the dialog's own load.

---

## 4. Tests added

| Test | Guards |
|---|---|
| `SubjectsLandingPageTests.ActivityGroupsFlagOff_TopicsStillLoadAndPageLeavesLoadingState` | Finding 1 — topics survive, no infinite spinner |
| `SubjectsLandingPageTests.ActivityGroupsFlagOff_DoesNotRequestTheActivityGroupsRoute` | No wasted 404 request |
| `SubjectsLandingPageTests.ActivityGroupsFlagOff_OwnerSelectOmitsTheActivityGroupOption` | Finding 2 |
| `SubjectsLandingPageTests.ActivityGroupsFlagOn_LoadsGroupsAndOffersTheActivityGroupOption` | No regression when enabled |
| `SubjectsLandingPageTests.ActivityGroupsRequestFails_TopicsStillLoadAndPageStaysUsable` | The isolation actually isolates |
| `SubjectsLandingPageTests.TopicLoadFails_ShowsErrorAndLeavesLoadingState` | The `Error` binding + `_items = []` |
| `SubjectsLandingPageTests.NonRealTenant_ShowsTenantPromptAndCallsNoApis` | Pre-existing guard |
| `AssignmentCreateBunitTests.Create_ActivityGroupsFlagOff_LoadsGradeLevelsAndHidesSelectedGroups` | Finding 1 on the wizard |
| `AssignmentCreateBunitTests.Create_ActivityGroupsFlagOff_DefaultMarkupStillRenders` | Gate does not break render |
| `JoinGroupsDialogTests.JoinDialog_ActivityGroupsFlagOff_SaysFeatureDisabledAndSkipsRoute` | Readable message, not a raw 404 |
| `TopicCreateDialogTests.CreateDialog_ActivityGroupsFlagOff_StillOpensWithGradeOwner` | Finding 2 |
| `TopicCreateDialogTests.CreateDialog_ActivityGroupsFlagOff_PreseededGroupOwnerFallsBackToGradeLevel` | Finding 3 |

`dotnet build SchoolCollab.slnx` → 0 errors.
`SchoolCollab.Admin.Tests.Unit` → 563/563 passed.
`SchoolCollab.Assignments.Tests.Unit` → 674/674 passed.

---

## 5. Pitfalls

1. **A dark-launch flag 404s; it does not 403.** The API gates by *not mapping*
   the route group. Never infer "flag off" from a status code — ask
   `IFeatureFlagService` first.
2. **Never co-locate a gated load with a required one in a single `try`.** One
   unmapped route then silently empties the resource the user actually needs.
   See `Assignments/Create.razor`, where the grade-level picker disappeared.
3. **`Loading="@(_items is null)"` must be paired with `_items = []` on the
   failure path**, or the grid spins forever instead of showing `Error`.
4. **Bind the error.** `Error="@_error"` — an unbound `Error` parameter renders
   nothing, which is how a real failure looks like a hang.
5. **`Error="Error"` is a literal string.** `DialogShellFooter.Error` is a
   `string` parameter, so `Error="Error"` passes the *word*. Always
   `Error="@Error"`. Still unfixed in `ActivityGroupCreateDialog`,
   `ActivityGroupEditDialog` and `NotificationPolicyFieldEditDialog` — audit with
   `grep -rn -A4 '<DialogShellFooter' --include=*.razor src | grep -E 'Error='`.
6. **Gate the control, not just the call.** Hiding the option is what removes the
   dead affordance; guarding the request alone still leaves a picker that cannot
   be satisfied.
7. **A pre-seeded enum value needs a fallback.** Gating the option is not enough
   when a caller passes the gated value in — resolve it back to the always-
   available option on init.
8. **Two `Detail.razor` files exist** in the Students app (`Pages/Students/` and
   `Pages/Students/GradeLevels/`). Always qualify the path in comments; an
   unqualified reference sent this doc's own investigation to the wrong file.

---

## 6. Follow-ups

| # | Item | Status |
|---|---|---|
| 1 | `documents/configuration.md` §2 consumer-site table lists `EnableActivityGroups` for Admin / Assignments.Api / Students.Api but **not** these four client surfaces. Sync the consumer sites. | ⬜ open |
| 2 | Items 3–5 of pitfall 5 (`ActivityGroupCreateDialog`, `ActivityGroupEditDialog`, `NotificationPolicyFieldEditDialog`) still carry the `Error="Error"` literal-string bug. | ⬜ open |
| 3 | Pilot-tenant override (Phase 6.1) makes the enabled path real for `Hydeson School` only — worth a UI pass with the flag ON before general availability. | ⬜ open |
