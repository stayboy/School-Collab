# The teacher portal's scope closes at read / review / grade

Date: 2026-10-05 · Provenance: owner grill (third portal session), held the same day the
portal's shell and ward-page retirement landed (PR #304). Companion surfaces:
`documents/specs/teachers-ward-portal-prefab-plan.md` (§5 Phase 2/3, §7),
`documents/specs/keycloak-ui-auth-integration.md` (D19),
`documents/solution/ward-surface-decision.md`.

## Decision

`src/SchoolCollab.Portals` — the Prefab teacher portal — is the teacher's
**read / review / grade** surface. T5's boundary is **final**, not a milestone on the way to a
portal-side authoring experience:

> assignment list (tenant- and teacher-scope filtered) → review queue → submission detail →
> submission review/grade

Three consequences follow, each taken deliberately:

1. **Authoring does not migrate.** Create / edit / publish stay on the Blazor **Admin** host.
   T7's "the eventual migration target for the rest" is superseded.
2. **The Phase 3 OIDC item is retired, not deferred.** It is *unnecessary by construction*.
3. **The shared shell stays brand + titles.** `views/shell.py` is one composition function and
   nothing more.

## Findings this rests on (verified 2026-10-05, `main` `59db339d`)

| Fact | Evidence |
|---|---|
| The authoring surface is large and Blazor-native | `AssignmentAuthoring.razor` (+`.js`/`.css`), `Create`/`Edit`/`Detail`/`Index`, `QuestionEditorSection`, `QuestionGenerationSection`, `QuestionsDraftSection`, `QuestionReviewList`, `PublishDialog`, `ReassignSignerDialog`, `NotificationFailuresSection` — all under `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/` |
| …and it has exactly **one** host | `src/SchoolCollab.Admin/SchoolCollab.Admin.csproj:16` is the only `ProjectReference` to that RCL in the repository |
| The portal serves four teacher routes plus session plumbing | `src/SchoolCollab.Portals/app.py` — `/teacher`, `/teacher/assignments/{id}`, the submission detail, the review `POST`; plus `/`, `/auth/callback`, `/logout`, `/health`. No authoring route exists |
| The portal holds no credential and needs none | D19: opaque session-id cookie only, APIs reached via `X-Portal-Session` *as data*; no credential, token or secret reaches Python (D12/AC9/AC11) |
| Prefab is the wrong tool for a multi-step editor | Prefab has no router, no `@Body` outlet and no `@layout` selection (`PrefabApp` fields: `title`, `css_class`, `state`, `js_actions`, `theme`, `stylesheets`, `css`, `scripts`, `defs`) — the ar-23 Phase-0 finding |

## The alternative that was priced and rejected — **migrate authoring (or a slice)**

| | Consequence |
|---|---|
| **Cost** | Re-implementing a dialog-and-section-heavy FluentUI surface in Prefab without a router: several Tier-3 rounds, then **permanent dual maintenance** of two authoring UIs |
| **Benefit** | One surface and one look for a teacher who both authors and reviews |
| **Why rejected** | The benefit is real but smaller than the cost: authoring already works, and a second implementation is exactly the drift the plan's own risk table names and that the ward decision retired one day earlier. A **narrow** slice (publish, or due-date edits) remains available as a future, explicitly-scoped round — it was *not* ruled out on principle, only deferred to a decision of its own |

**The shell question** (`nav` or `theme`/`mode`?): left as-is. Four routes already carry per-page
breadcrumbs, and a visual pass belongs to a design review, not to a refactor's tail. No trigger set.

## Named triggers to revisit this decision

- A teacher-facing requirement that cannot be met on the Blazor Admin authoring surface.
- A visual/design review finding the Prefab portal's look inconsistent with the FluentUI product
  surface (then: a `theme`/`mode` pass, with the shell as the one place to set it).
- The portal growing a **second** consumer (then: reopen the OIDC question explicitly — the
  retirement above is *not* a permanent ban, it is a statement that nothing needs it today).

## Related corrections made in the same pass

- `documents/specs/teachers-ward-portal-prefab-plan.md` — §5 Phase 2 (scope-closure bullet),
  §5 Phase 3 (OIDC retired), §6 (the OIDC risk row marked historical), §7 item 2 (all three
  remaining flags closed), §7 item 5 (the shell stance), the T7 row, and the Status/Date lines.
- `documents/specs/keycloak-ui-auth-integration.md` — three clauses the **ward decision** made
  moot are annotated, not rewritten (each was true when written): §2's portalled-exclusion
  parenthetical, §16 item 5, and the tail of the D19 row.
- `documents/specs/assignment-authoring-compartments.md` — the top blockquote's "the portal is the
  eventual migration target for the working assignment feature set" is struck through and
  annotated. (Outside the guarded *Deferred / known gaps* section, so
  `AssignmentAuthoringSpecGapsTests` is untouched.)
- `documents/solution/ward-surface-decision.md` — its "Open, carried to the next round" bullet
  (which pointed at plan §7.2) is closed with a pointer here.

## Pinned in the same change

The portal's **route set is pinned** — by execution, not by a .NET architecture test: the
portal is Python, so the pin lives where the app object is importable.
`test_route_set_is_the_reviewed_set` (`src/SchoolCollab.Portals/tests/test_app_routes.py`)
asserts the live route table equals the reviewed eight-route set, pins FastAPI's four own
routes so a version bump is reported as a framework change, proves the write surface is
exactly the one review POST, and verifies the three route constants (`REVIEW_PATH`,
`AUTH_CALLBACK_PATH`, `LOGOUT_PATH`) still resolve to their pinned paths — `app.py`'s outcome
handler matches on `route.path == REVIEW_PATH`, so a drifting constant would break that match
silently otherwise. Probed red-then-green before commit: an injected
`GET /teacher/assignments/new` failed the pin; the revert passed. CI-gated by the
`portals-python` job, so a new route in the portal is a deliberate, reviewed act — add or
remove it from `REVIEWED_ROUTES` in the same change. (The `.slnx` item set stays guarded
separately, by `PortalsSolutionItemsArchitectureTests`.)
