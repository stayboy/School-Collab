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

   - Add `bunit` packages to the test project that owns the component if they are not
     already present.
   - Register required services (`NavigationManager`, dialog/toast providers, HTTP
     clients, etc.) in the bUnit `TestContext`.
   - Assert the bug-specific UI outcome, such as route discovery, expected headings,
     buttons, empty states, error boundaries, or disabled actions.

5. **No untested bug fixes.** If a bug cannot be tested directly, document why in the PR
   and add the closest available coverage, such as routing, service, or component
   integration coverage.

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
