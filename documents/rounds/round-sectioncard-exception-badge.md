# Round — SectionCard exception-count badge (enrollment exceptions, grade Subjects card)

**Mode: Solo** — owner-directed, presentation-only change (no logic, no schema, no API). Spec of
record: `documents/specs/subject-period-exception-model.md` §5.2. Frozen patch:
`documents/rounds/diffs-sectioncard-exception-badge.patch` (code + tests only).

---

## 1. What the owner asked for

> "SectionCard for topics doesn't indicate exception count badge, on topic or subject, if any exists"
> "Put the badge next to subject text. Not a selector. keep menu item as-is"
> "Show as 'SubjectText (2)', with count in a badge"
> "use badge color - red or danger"

## 2. The gap (verified before changing anything)

The grade-detail **Subjects SectionCard** did render the count — but as **plain meta text**:
`Detail.razor`'s `SubjectMeta` appended `EnrollmentExceptionLabels.FormatCount(n)` to the
`ItemMetaSelector` line, and `SectionCard.razor` renders that as `.item-meta__count` (hint-grey,
`|`-separated — `SectionCard.razor.css:109-119`). The two sibling surfaces render a real badge:
`GradeTopicsDialog.razor:52` (View-all) and `Subjects/Subjects.razor:90` (Topics landing), both
`FluentBadge Appearance="Accent"`.

`GradeLevelDetailPageTests.Detail_TopicsCard_SubjectWithTwoExceptions_…` asserted only the *string*
`"2 exceptions"`, not a badge element — which is why the missing badge shipped. The dev DB carried a
live exception (English Home Language, `Terms`/1st) on a topic assigned to that grade, so the state
was reachable.

## 3. Change

| File | Change |
|---|---|
| `SectionCard.razor` | **new** `ItemNameSuffix` parameter — a `RenderFragment<TItem>` (markup, **not** a selector); the item name now renders inside `.item-name-row` with the suffix after it |
| `SectionCard.razor.css` | `.item-name-row` flex row + `.item-name` becomes `flex: 1 1 auto` (was `width:100%`) so the suffix lands on the name line |
| `Detail.razor` | the count moves OUT of `SubjectMeta` into the slot; `SubjectMeta` is now strand/lesson counts only |
| `EnrollmentExceptionLabels.cs` | doc updated (the card shows a **name badge**, not a meta-line part) |
| `SectionCardTests.cs` | +1 test: the suffix renders inside `.item-name-row` and is absent from the meta line |
| `GradeLevelDetailPageTests.cs` | the badge assertion is now element-level, plus the display form and the absence from the meta line |

Rendered shape (the kebab is untouched):

```razor
<ItemNameSuffix Context="t">
    @if (ExceptionCount(t.TopicId) is var exceptionCount and > 0)
    {
        <span class="item-name-count">(<FluentBadge Color="Color.Red" Title="@EnrollmentExceptionLabels.FormatCount(exceptionCount)">@exceptionCount</FluentBadge>)</span>
    }
</ItemNameSuffix>
```

Three details are load-bearing rather than cosmetic:

- **The count is the badge; the parentheses are plain text** — so the red applies to the number only,
  and the row reads `Mathematics (2)` exactly.
- **The wrapper span keeps the token ONE flex item.** `.item-name-row` is a flex row with a
  `0.5rem` gap, so a bare `( badge )` would gain spaces around each piece. The markup is also kept on
  one line so no whitespace text node lands between the pieces.
- **`Color="Color.Red"`** — `FluentBadge` has no danger `Appearance` in 4.14.2 (the enum is
  Accent/Neutral/Lightweight/Outline/Hypertext/Filled/Stealth), but it does expose the palette
  `Color`. `Color="Color.Red"` is already the repo's precedent
  (`SchoolCollab.Families/…/Ward/Index.razor:46`). Red is the danger signal: an exception is the
  subject **not** being offered — a restriction, not a highlight.

The tooltip keeps the shared `EnrollmentExceptionLabels.FormatCount` string (`"2 exceptions"`), so
the one-spelling rule still holds and accessibility does not lose the word "exceptions".

## 4. Verification (parent-run, in-session)

| Check | Result |
|---|---|
| `dotnet build` — `SchoolCollab.Students.Application` | **0 errors / 0 warnings** |
| `Admin.Tests.Unit` | **596 / 596** (595 → +1 new `SectionCardTests` case) |

**Not run: the full-solution gate.** `dotnet build SchoolCollab.slnx` fails MSB3021/MSB3027 — the
running `SchoolCollab.Admin`, `Students.Api` and `Assignments.Api` processes plus Visual Studio hold
their bins. The changed library and the test project were built directly
(`-p:BuildProjectReferences=false` for the test project) to verify. The last full-solution run
(before this change) was **2975 / 2972 / 3**, the same 3 environmental `ChatAsync_WithOpenRouter_*`
failures.

**No browser pass** — the badge's rendered geometry is static-verified only (bUnit asserts the DOM
structure: badge in `.item-name-row`, text `2`, display `(2)`, absent from `.item-meta`).

## 5. Residual / not in scope

- The **View-all dialog** and **Topics landing** badges remain `Appearance="Accent"` with the
  `"N exceptions"` text. The owner asked only about the SectionCard, so they were left alone; matching
  them is a one-line change each if wanted.
- The badge is rendered only when `ExceptionCount > 0` — the absence of a badge stays the normal
  state (§5.2).
