# Component-first reuse policy

> **Status:** **ADOPTED (owner, 2026-10-25)** — settled as a whole ("all as recommended") on the round that
> asked Q21–Q25. Q21(a) extraction trigger, Q22(a) `RailBand` first, Q23(a) measured cost guardrail,
> Q24(a) approval gate, Q25(a) this file + one `AGENTS.md` row. Filed as issue #415.
> **Owner:** repository-wide (applies to every `.razor` surface in `src/`).
> **Relates to:** `documents/solution/panel-layout-pattern.md` (the block/region vocabulary this policy
> composes with), `documents/specs/subject-enrollment-requirement-flag.md` (round B, the first surface that
> will consume the components proposed here), `AGENTS.md` § "Reuse before new infrastructure".
> **Evidence:** the students create/edit round (2026-10-24 → 2026-10-25) — see §5.

---

## 1. The policy in one paragraph

**Prefer a component over repeated markup.** When the same visual or interaction pattern appears — or is
about to appear — in more than one place, extract it as a named component rather than copying markup, because
a component makes the call site read as intent ("a labelled band of chips") instead of as structure
(`<div class="rail-band"><div class="rail-band__label">…`). Extraction is **not** unconditional: it must earn
its keep, it must never be speculative, and a component that needs its own data fetch, JS interop or DI is a
feature in disguise, not a reuse. When the cost is likely to outweigh the gain — a page already dense with
components, or a single call site with no planned second — the correct answer is to **advise the owner against
it with the numbers**, not to extract anyway.

---

## 2. When to extract (the trigger)

Extract at the **second** occurrence, not the first:

| Trigger | Extract? | Why |
|---|---|---|
| The pattern is in **≥2 call sites** today | **Yes** | Repetition is the only evidence that a shape is stable. |
| A **design or spec** names the surface (a mock's "banner", a spec's "Subjects section") | **Yes, after approval** (§4) | The design already treats it as a unit; copying it by hand guarantees drift between the mock and the code. |
| **≥2 call sites are planned inside the same round** (round A bands → round B bands) | **Yes** | The second call site is committed work, not a guess. |
| **One** call site, no second in sight | **No** | Speculative abstraction is the most expensive kind of component: it carries props nobody uses and changes shape the moment the second caller arrives. |
| The shape is **already a shared primitive** (`PanelSection`, `RowForm`, `DropdownForEnum`, `CodedValueDropdown`, `RowActionsMenu`, `SectionCard`, `DialogShellBase`, `DialogShellFooter`, `AuthoringShell`, `TenantGate`) | **Reuse it — do not wrap it** | A wrapper around a shared primitive is a second name for the same box; the panel pattern's §3 "one box language" applies to components too. |

---

## 3. Where it goes

| Reach | Location | Requirements |
|---|---|---|
| Cross-context / shared chrome | `src/SchoolCollab.Admin.Shared/Components/` | `.razor` + `.razor.css`; XML doc comment naming **every call site** and the pattern/spec section it implements; no context-specific DTOs in its parameters (pass primitives/records the Shared project already knows) |
| One bounded context, several pages in it | that context's `Application/Components/` | same doc-comment rule |
| One page only, genuinely not reused | inline in the page, or a page-local `.razor` | state the reason in the round doc ("single call site; no second planned") |

A component **must not** be created in `Admin.Shared` to serve one page "for symmetry". Symmetry is not a
call site.

---

## 4. Approval gate for design- and spec-derived components

A component that exists because a **design (mock) or a spec** named the surface goes through the same gate the
mock itself does:

1. **Grill round first.** The component's shape, name, parameter set and call sites are put to the owner in one
   numbered round with recommendations — never decided inside implementation.
2. **Mock renders it.** The mock must draw the component's states (rest / empty / blocked / dense), because a
   component's contract is invisible in prose.
3. **Contract recorded.** The round doc records: name, parameters (each with its call site), the seam ids it
   owns (`#student-edit-activity-groups` style, for tests), and its accessibility contract (role, labels,
   keyboard).
4. **Two-call-site rule at birth.** A design-named component may be created with **one** implemented call site
   if the design commits a second — that commitment is named in the round doc, and the second lands in the same
   or the immediately following round. Otherwise the owner grants an explicit exemption.

---

## 5. The cost side — when to advise against components

Extraction has three real costs. Each one is a reason to **report and recommend against**, with numbers:

1. **Render cost.** A page that composes dozens of components re-renders each of them on state change. Before
   adding a component to an already-dense surface, measure: if the page's component count for that kind is
   already high and the markup is static, keep it as markup. Blazor's diffing is cheap per element but not
   free per component; the honest test is a render measurement (bUnit's `RenderCount`, or a `Stopwatch` around
   `StateHasChanged` in a probe), not intuition.
2. **CSS-isolation reach.** A class applied to a child component's **root** element is not reachable from the
   parent's scoped stylesheet — the parent's rules silently match nothing. This is not theoretical: the contacts
   add-row's `.contacts-value { flex: 1 1 0 }` never applied for exactly this reason, and the input sat at its
   intrinsic 185px inside a 586px row until `::deep` was added (2026-10-25). Every new component that a parent
   will want to size must document how it is sized (parameter, CSS variable, or a documented `::deep` seam).
3. **Indirection.** A component that fetches its own data, injects a service, or owns JS interop is a feature
   with a lifecycle. Those belong to a page or a dialog, never to a "reusable presentational" component — a
   presentational component takes values and raises callbacks, nothing else.

**Advice rule.** If two of those three costs are present, the reply recommends **against** extraction and names
the alternative (a partial, a `RenderFragment`, or plain markup with a class), with the measurement attached.

---

## 6. Round-A evidence (why this policy is not abstract)

The students enrolment work produced both sides of the argument inside one round:

| Observation | Consequence in this policy |
|---|---|
| The edit rail's **enrolment bands** (placement, activity groups) were written inline — ~50 lines of `.rail-band` / `.rail-chip` markup — and round B will add two more bands (enrolment-required, assumed) plus the Enrol dialog's placement banner with the same shape | §2: extract `RailBand` **now**, at the second committed call site |
| The **contact add row** needed `::deep` because its classes landed on a child `FluentTextField` root | §5.2: a new component must document how parents size it |
| `ContactsEditor` carries its own API client and dialog service | §5.3: it is a feature component, correctly not "reused" as presentational markup |
| A `badge`/`chip` shape now exists in three places (rail chips, picker badges, panel count) with three different rule sets | §2 trigger is ≥2 **call sites**, not ≥2 **similar looks** — unify only when the call sites' semantics agree |

---

## 7. What this policy does not do

- It does not mandate a design system or a component library rewrite.
- It does not create a CI gate: extraction is a judgement call, and a lint rule cannot see a call site that does
  not exist yet. The checkable parts (a component in `Admin.Shared` with no call site; a `.razor.css` rule that
  can never match) are candidates for `SchoolCollab.ArchitectureTests.Unit` **later**, once this policy is
  ratified and has fired twice.
- It does not restate `panel-layout-pattern.md`. Where the two meet (a block is one box; a band is not a box),
  the pattern wins and this policy points at it.
