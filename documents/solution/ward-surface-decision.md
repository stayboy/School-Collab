# Ward surface decision — Blazor (`SchoolCollab.Families`), not the Prefab portal

- **Decided:** 2026-10-05, by the owner, in the `grill-me` round that answered the
  `documents/specs/teachers-ward-portal-prefab-plan.md` Phase-1 / Phase-4 gate.
- **Status:** DECIDED. Phase 1 (the Prefab "ward portal MVP") is **retired — already
  delivered in Blazor**. Phase 4 ("go/no-go vs extending the Blazor Families host") is
  **decided early, in favour of Blazor**.
- **Record of:** *why* the ward surface is not built in the Prefab portal — the finding before
  the decision, per this folder's "Finding → Implementation" standard. The plan was annotated
  in the same change set and remains the source of truth for the portal's remaining scope
  (the teacher/staff surface).

## The question

The plan deferred a re-decision to "before any MVP work" (its Q6 row; the file spelled the same
gate "Q5" in places, which was a mis-numbering — Q5 is *Serving*). Its Phase 1 was "Ward portal
MVP": assignment list → module progress ("mark watched") → submission, over the existing ward
REST endpoints, with dev-bypass auth. §7.1 named the spike-success criteria — Prefab
reactivity/form/table ergonomics — as the thing to evaluate first. The trigger was "after the
teacher write half lands", fired by round `portal-submission-grade`.

## Findings

1. **Phase 1's scope had already shipped in Blazor, before the gate was answered.** The
   AR-13/14/15 train (#236/#237/#239, all merged to `main`) delivered:
   `src/SchoolCollab.Families/Components/Pages/Ward/Index.razor` (assignment list over
   `WardAssignmentListItemDto`, with completion state); `Ward/Assignment.razor` (video modules,
   `Api.ReportModuleProgressAsync` progress/"mark watched", questions, `Api.SubmitAsync`
   submission, result card); and, beyond Phase 1's bullets, `DeepLink/` landing
   (`DeepLinkEndpoints.cs`, `DeepLinkLandingService.cs`), `Ward/SignOff.razor` (guardian
   e-sign) and `wwwroot/js/wardPlayer.js`. Building Phase 1 in Python would have written a
   second implementation of a shipped, tested surface.
2. **Prefab cannot replicate Blazor's layout/router mechanism.** Verified against the installed
   `prefab_ui` 0.20.2 and its docs: there is no router, no `@Body` outlet and no `@layout`
   selection; the component set ships no `nav`/`sidebar`/`header`/`layout` module. `Page`/`Pages`
   swap content *via state inside one document*, `Slot` renders a tree from a state key, and
   `OpenLink` is a full navigation. A Blazor-equivalent layout is therefore a **Python
   composition function** plus `PrefabApp`'s document-shell fields (`theme`, `stylesheets`,
   `css`, `scripts`, `defs`) — the same *outcome*, a different *mechanism*, and no route table.
3. **The ward flow is the worst fit for that gap.** It is navigation-heavy (list → player →
   deep-link landing) and media-heavy (video), i.e. exactly where a missing router and
   state-swapped pages cost most. By contrast the teacher workspace — a small, form-and-table
   surface — is where Prefab's model fits and where it has already proven itself (session auth,
   a write half, CI, forms).
4. **The portal's ward page is a spike artifact with no product intent behind it.**
   `src/SchoolCollab.Portals/views/ward.py` exists because Phase 0's exit criterion was "one
   live API call rendered as a Prefab view"; it is also the portal's `/` route
   (`app.py:752`). It carries three tests in `tests/test_app_routes.py` and two D19-scope tests
   in `tests/test_teacher_session.py`, a `SchoolCollab.slnx` row and a
   `PortalsSolutionItemsArchitectureTests` pin — maintenance for a surface no product path leads
   to.
5. **The cost was already named in the plan.** Its own risk table carries "Dual-stack surface
   drift (Blazor Families vs Prefab portal)" with "Phase 4 decision review" as the mitigation —
   this decision *is* that review, taken early.

## Decision

**The ward surface stays Blazor.** The Prefab portal is the **teacher/staff surface only**.
Phase 1 is retired as already delivered; Phase 4 is decided in favour of extending
`src/SchoolCollab.Families`. The portal's ward spike page retires with the decision.

## Alternatives rejected

- **Full Prefab ward MVP (Phase 1 as written).** Thin information gain — it would re-prove what
  the teacher surface already proved about Prefab — while duplicating a tested surface and
  creating precisely the drift risk finding 5 names.
- **A narrow evidence round** (seed a real ward assignment, render it, fire the two ward POSTs,
  then judge ergonomics). Cheaper, but its only open question — Prefab ergonomics on real data —
  cannot change this decision, because Blazor already ships the product.
- **Deferring again.** There was nothing left to learn; a second deferral would have parked a
  decided question behind another trigger.

## Consequences and follow-ups

- **Retirement round** (pending): delete `views/ward.py`, its `/` route, the `build_error_view`
  import, `_teacher_surface_for`'s "`None` for the ward page" branch, its five portal tests, and
  the `.slnx` row + guard pin — and answer what `/` becomes (recommended `302 → /teacher`).
- **The teacher portal's shared shell** is the surviving surface's own gap: seven hand-rolled
  `PrefabApp(title=…, css_class="p-6")` call sites across `views/teacher.py` (+ `views/ward.py`).
  A `views/shell.py` composition function is the Prefab analogue of `MainLayout`'s outcome; it
  must add a `.slnx` row **and** the `PortalsSolutionItemsArchitectureTests` reviewed-set pin in
  the same change.
- **Accepted dependency (unchanged by this decision):** the Prefab renderer loads from a
  CDN (jsDelivr, version-pinned) at page load. Accepted with a named trigger — any
  offline/air-gapped requirement or supply-chain policy — recorded in the plan's risk table.
- ~~**Open, carried to the next round:** whether any OIDC work remains for the portal now that the
  D19 portal-session path gives it a credential path, and the migration order for the remaining
  assignment create/edit/publish features (plan §7.2).~~ **CLOSED 2026-10-05 (same day, scope
  grill):** the OIDC item was **retired as unnecessary by construction**, and the scope **closed at
  read/review/grade** — authoring does not migrate to the portal. See
  `documents/solution/portal-scope-decision.md`.

## Provenance

- `documents/specs/teachers-ward-portal-prefab-plan.md` — the plan (annotated 2026-10-05: Status
  line, Date line, Q4 row, §4 sketch + API contract, Phase 1, Phase 3, Phase 4, risk table, §7).
- `documents/rounds/round-ar-23-ward-portal-prefab-spike.md` — the Phase-0 spike and its
  residuals (ephemeral round artifact; its durable findings are folded into the plan).
- `documents/specs/keycloak-ui-auth-integration.md` D19 + round `portal-session-adoption` — the
  portal's credential path (the "no credential path" prerequisite is discharged).
- `tests/SchoolCollab.ArchitectureTests.Unit/PortalsSolutionItemsArchitectureTests.cs` — the
  `.slnx`/spec coupling that any portal file addition or removal must satisfy.
