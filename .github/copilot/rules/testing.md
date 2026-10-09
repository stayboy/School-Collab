# Testing Rules

This file contains topic-specific guidance for bug-fix regression tests and unit tests
for feature additions.

## Test Platform & Frameworks

The repository has adopted **Microsoft Testing Platform (MTP)** as the primary test execution engine for .NET 10.

### Framework Standards
- **Primary Framework**: **MSTest** is the preferred framework for all new unit and integration tests.
- **Legacy Frameworks**: xUnit and NUnit are maintained for existing projects but should not be used for new development.
- **Tooling**: Use **Moq** for mocking and **FluentAssertions** for assertions.
- **Component Testing**: Use **bUnit** for Blazor components.

### MTP Configuration
All test projects must be MTP-compatible:
1. Set `<OutputType>Exe</OutputType>` in the `.csproj`.
2. Use the `global.json` runner configuration: `{ "test": { "runner": "Microsoft.Testing.Platform" } }`.

---

## Bug-fix regression tests

Every bug fix must include a regression test that proves the reported bug is fixed. Do
not treat a bug fix as complete when it only changes production code.

### Rules

1. **Write the regression test first when practical.** The test should fail against the
   buggy code and pass after the fix. If reproducing the exact failure is too expensive,
   add the smallest test that covers the fixed behaviour and explain the trade-off in
   the PR description.

2. **Run the relevant test project after the fix.** At minimum, run the test project
   that owns the changed production code before committing. If the fix crosses projects,
   run all affected test projects.

3. **Backend and domain bugs.** Add or update unit/integration tests using the existing
   MSTest/Moq/FluentAssertions patterns. API/client bug fixes should include HTTP
   status, payload, and error-path coverage where applicable.

4. **UI and Blazor component bugs.** Use **bUnit** tests for Razor/Blazor component
   regressions. Test the rendered component tree and user-facing behaviour, not only
   private methods or view models.

5. **FluentUI components render web components, not native controls.** A `FluentCheckbox` renders
   `<fluent-checkbox>`, so there is no `input[type=checkbox]` in bUnit markup to `.Change(...)`. Drive
   it through the component instance — `FindComponents<FluentCheckbox>()` then
   `InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(true))` — and assert on the tag, the
   component's parameters (`box.Instance.Disabled`) or the surrounding markup. The same applies to
   every Fluent control backed by a web component (`EnrollmentExceptionsDialogTests` is the
   precedent).

   - Add `bunit` packages to the test project that owns the component if they are not
     already present.
   - Register required services (`NavigationManager`, dialog/toast providers, HTTP
     clients, etc.) in the bUnit `TestContext`.
   - Assert the bug-specific UI outcome, such as route discovery, expected headings,
     buttons, empty states, error boundaries, or disabled actions.
   - **Dialog/component mutation handlers (add / assign / link / remove) must have a
     bUnit test asserting the local list state updates.** A handler that fires its
     callback but never updates the component's own `[Parameter]` list snapshots is a
     bug — the UI won't reflect the change until the dialog is reopened. Test that the
     added item appears in the list and leaves the picker (e.g. `Teachers` gains the
     linked teacher and `UnlinkedTeachers` drops it).
   - **FluentUI web components cannot be driven with generic bUnit events.**
     `TriggerEvent("oncheckedchange", new ChangeEventArgs{…})` throws inside the
     page's `ErrorBoundary` (`ChangeEventArgs` cannot convert to
     `CheckboxChangeEventArgs`), which reads as a page defect when it is a test
     defect. Drive the bound callback instead:
     `cut.InvokeAsync(() => cut.FindComponent<FluentCheckbox>().Instance.ValueChanged.InvokeAsync(true))`
     (the `InvokeAsync` wrapper is required — invoking off the renderer thread
     raises *"not associated with the Dispatcher"*).

5. **No untested bug fixes.** If a bug cannot be tested directly, document why in the PR
   and add the closest available coverage, such as routing, service, or component
   integration coverage.

---

## bUnit pitfalls — render crashes, dialog round-trips, element-id seams

Distilled from the assignment-authoring rounds (2026-10); each entry is a
verified failure, not a theory.

### A render-time exception looks like a FLAKY TIMEOUT

Any page that wraps content in `<ErrorBoundary>` (the repo rule) converts a
render-time exception into the fallback UI instead of a test failure. In bUnit
this surfaces as `WaitForFailedException` — "the assertion did not pass within
the timeout period" — with the classic signature: the failing tests **pass in
isolation and fail together** under full-suite load. The most common cause seen
in this repo so far is an `Icon` instance passed to the generic `Icon=` parameter
(`.github/skills/fluentui-icons/SKILL.md` § "The instance trap"). Diagnosis:

1. Temporarily widen the page's `ErrorContent` from `@ex.Message` to `@ex.ToString()`.
2. Run the single failing test; read the inner exception in the rendered fallback.
3. Revert the widening — never commit the widened form.

### Dialog round-trip tests need one named, generous budget

Show → interact → submit through the real dialog shell (harness below) is
load-sensitive: under a full-suite run the renderer can take tens of seconds to
build the dialog. Use ONE named budget constant for every round-trip wait in a
test class — e.g. `private static readonly TimeSpan DialogRoundTripBudget =
TimeSpan.FromSeconds(15);` — passed to the wait helpers, instead of short
hard-coded timeouts. Flake signature: the dialog's list is still empty after 60+
poll checks while renders are still arriving — that is load, not a defect.

### Element ids are the bUnit contract

- Stable element ids on authoring/form surfaces (`authoring-basics-title`,
  `scoringFieldsPassScore`, `cq-prompt-count`, …) are test seams. **Never rename
  one in a refactor** without updating every assertion; prefer adding a new id
  over reusing an old one.
- Field ORDER is a contract: assert it with `ContainInOrder` over an id
  sequence, and update the sequence (with a message stating the new order) in
  the same change that moves the field.
- When an element is deleted, keep its seam as an **absence** assertion —
  `cut.FindAll("#old-id").Should().BeEmpty("why it is gone")` — instead of
  deleting the test. Deleting the assertion deletes the regression guard.

### Two sanctioned dialog harnesses (and the beforeunload trap)

- **End-to-end (preferred for write paths):** the real `IDialogService` plus a
  rendered `FluentDialogProvider` —
  `DialogService.ShowShellDialogAsync<TDialog, TModel, TResult>(...)`, then
  `provider.WaitForAssertion(() => provider.Find("form"))`. Exercises
  show → interact → submit for real (`QuestionPromptDialogBunitTests`).
- **Write-back only:** a mocked `IDialogService` returning a fixed OK payload
  (`Mock<IDialogReference>` + `DialogShellResult<T>` registered in the test's
  DI) — for asserting the caller applies the result
  (`ContextPicksSectionBunitTests.RegisterContextPickDialog`).
- **Beforeunload trap:** any page test whose page carries a collocated
  `beforeunload` JS module must call
  `JSInterop.SetupModule(<the page's module-path const>)` before rendering, or
  every render awaits a JS import that never answers and the test hangs
  (`Authoring.BeforeUnloadModulePath` precedent; JSInterop Mode = Loose).

---

## Unit and Integration tests for feature additions

Every new feature, service, or behavioural class **must** include tests in
the corresponding test project. Tests go in a file named after the class
under test (e.g. `ChatClientFactoryTests.cs` for `ChatClientFactory.cs`).

### Rules

1. **Add tests alongside new code.** A PR that adds a new class with behavioural logic
   (routing, validation, text cleaning, mapping, etc.) must also add a corresponding
   test file or extend an existing one. Pure data-transfer objects (DTOs, records) and
   trivial wrappers (delegates, thin extension methods) are exempt.

2. **Test file naming.** `<ClassName>Tests.cs` — one test class per production class.
   Keep tests in the project root namespace unless a
   `Domain/` subfolder matches the production namespace.

3. **Framework.** Use MSTest (`[TestClass]`/`[TestMethod]`), Moq for mocking, and
   FluentAssertions for assertions.

4. **Coverage targets.** At minimum, test:
   - **Happy path** — the primary use case works correctly.
   - **Edge cases** — null/empty inputs, boundary values, case sensitivity.
   - **Error/fallback paths** — what happens when a dependency is missing or returns an
     unexpected result.
   - **Routing/branching logic** — every `if`/`switch` branch must have at least one
     test that exercises it.

5. **API Integration Testing Patterns.** When testing API endpoints:
   - Use `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<TProgram>`) to host the API in-memory.
   - **Auth Bypass Verification**: If a feature is guarded by a feature flag (e.g. `FEATURE:DisableOIDCAuth`), write tests for both states: one where the flag is enabled (verifying anonymous access) and one where it is disabled (verifying 401/403 response).
   - **Contract Testing**: Assert on the exact JSON structure of the response and the correct HTTP status code.
   - **Tenant Isolation**: For operational data, verify that requests with different tenant headers/claims do not leak data between tenants.

6. **Run tests before committing.** `dotnet test` must pass with 0 failures before a PR
   is submitted. If existing tests break, fix them in the same commit.

7. **Reference the production project.** Ensure the test project references
   the relevant production project via `<ProjectReference>`.

8. **`InternalsVisibleTo`.** If the class under test is `internal`, ensure the
   production project has
   `<InternalsVisibleTo Include="Your.Test.Project.Name" />` in its
   `.csproj`.

9. **HTTP 404 handling pattern.** When an API client method calls an endpoint that may
   return 404 (e.g. "get by code" or "get by id"), the method must check
   `response.StatusCode == HttpStatusCode.NotFound` and return `null` instead of
   throwing `HttpRequestException`. Never use `GetFromJsonAsync<T>()` for endpoints that
   can return 404 — it throws on non-success status codes. Use `GetAsync()` + status
   check + `ReadFromJsonAsync<T>()` instead.

---

## SectionCard testing

The shared `SectionCard` component's rendering contract is covered **once** in
`tests/SchoolCollab.Admin.Tests.Unit/SectionCardTests.cs` (title/count, empty, loading,
error, top-N, selectors, href/click, tooltip, kebab, add, view-all, ItemTemplate).

- **New SectionCard capabilities must be covered in `SectionCardTests.cs`** — the shared
  component test — not re-implemented per card.
- **Page tests** (`GradeLevelDetailPageTests.cs`) stay focused on **wiring**: which
  handler/selector each card binds, per-card selectors, and the mutation-handler
  local-state assertions (rule 4 above). Do not re-test SectionCard rendering mechanics
  in the page tests — that is 4× duplicated logic that belongs in `SectionCardTests.cs`.
