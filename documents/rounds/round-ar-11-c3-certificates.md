# Round ar-11-c3-certificates — C3: QuestPDF certificate on finalize + download route (Phase 3 remainder, WS-C last item)

Provider: pi. **EXECUTED 2026-09-15 as a LIGHT ROUND (Tiers 1–2: parent plans + one worker + static diff-only reviewer; NO UI tester) — owner menu choice ("don't merge yet. skip to step 2. Use light round as recommended").** Tier-3 UI-tester scope is skipped by owner choice; the tester handover below stays recorded for a future pass.
**Round base: `a5f49efd` (stack/10-ar-10 tip; NOT main — #232 unmerged by owner instruction; stack/11 shares files with unmerged ar-10, so the branch was cut from the stack tip). Branch `stack/11-ar-11-c3-certificates` @ base at dispatch.** Pre-round carryover in the working tree (NOT part of the round diff; patch freeze excludes it): `documents/solution/assignment-request-implementation-details.md`, `documents/solution/assignment-request-go-forward-breakdown.md` (ar-10 doc catch-up), `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCreateBunitTests.cs` (ar-10 CI-flake diagnostic wrapper), untracked round/scratch docs. Resolvable subagent ladder (from `~/.pi/agent/models.json`, per the ar-9/ar-10 quirk note): worker `ollama/deepseek-v4-flash:0731-cloud` → reviewer `ollama/kimi-k2.7-code:cloud`. Light round = no tester dispatch.

- **Tier:** 3 checks (UI round), but a SMALL round: ~19 expected files, ONE bounded context (Assignments) + Admin-less; **ZERO migrations** (`SignatureEvent.CertificateStoragePath` shipped nullable in ar-9 — this round populates it), **ZERO new feature flags**, **ZERO new AppHost params** (`IFileStore` options shipped in ar-4), **ONE new CPM package** (QuestPDF).
- **Sources:** `documents/solution/assignment-request-implementation-details.md` §2 WS-C certificate bullet ("C3; D-1/D-3 decided — local-FS `IFileStore` + QuestPDF: generate on Finalize → `IFileStore` path → reference in `SignatureEvent`; download endpoint `GET /assignments/{id}/students/{sid}/certificate`") + §3 row "9+"; `documents/solution/assignment-request-go-forward-breakdown.md` §4 Phase 3 C3 item + §3.2 "Certificate PDF" row; `documents/specs/assignment-request-feature-spec.md` §3.2 (audit line 53) + §5 (`SignatureEvent` line 96); `documents/rounds/round-ar-9-signoff.md` (the Out-list + the "CertificateStoragePath ships nullable + unread — the C3 certificate round populates it" residual); skills `use-js-interop`, `dotnet-best-practices`, `blazor-components`, `fluentui-icons-in-school-collab`.
- **Drift-refresh facts (verified 2026-09-15 against the ar-10 branch tip — all seams present):**
  - `Core/Domain/SignatureEvent.cs` — `CertificateStoragePath string?` nullable; **no update methods** (ar-9 append-only posture — decision (c) adds the single narrow exception below).
  - `Core/CQRS/.../Commands/SignOff/FinalizeSignOffCommandHandler.cs` — loads ONLY the submission (`ISubmissionRepository.GetSubmissionByAssignmentStudentAsync`), then `submission.FinalizeSignOff()` (Signed-only guard, stamps `FinalizedAt`).
  - `ISubmissionRepository.GetSignatureEventByAssignmentStudentAsync(assignmentId, studentId)` exists (ar-9); `IAssignmentRepository.GetAsync` (assignment title); `IStudentDirectory.GetStudentNameAsync`/`GetGuardiansAsync` (names degrade to the raw id on miss — the `GetSignOffContextQueryHandler` precedent); `ISignatureConsentTextResolver.ResolveConsentTextAsync`.
  - `Core/Services/IFileStore.cs` — `StoreAsync(Stream, fileName, contentType)` → opaque path; `OpenReadAsync` (throws `FileNotFoundException` when missing); idempotent `DeleteAsync`. `LocalFileStore` lives in **Core**/Services.
  - Routes: `POST /{id}/students/{studentId}/sign-off/finalize` (~line 849) + `GET /{id}/students/{studentId}/sign-off` (~871) in `AssignmentRoutes.cs`; `SignOffRoutesTests.cs` is the minimal-TestServer route-test harness precedent.
  - UI: `Detail.razor` "Guardian Sign-off" tab (~line 326); `SignOff.razor` guardian e-sign page; **NO `wwwroot` JS exists yet in Assignments.Application — this round adds the app's first JS interop** (use the `use-js-interop` skill: dynamic `import()`, module cached on the component, `IAsyncDisposable`).
  - Tests present (additive targets): `ReassignFinalizeSignOffCommandHandlerTests.cs` (owns the finalize handler tests — worker verifies the exact home of finalize cases and reports a deviation if different), `SignatureEventTests.cs`, `SignOffRoutesTests.cs`, `SignOffPageBunitTests.cs`, `AssignmentDetailBunitTests.cs`.
  - QuestPDF: pin **2026.8.0** (2026.9.0 released 2026-09-14 with font-system breaking changes — the worker may NOT bump past 2026.8.0 without owner instruction). Community license applies (org <$1M): `QuestPDF.Settings.License = LicenseType.Community`.

### Plan amendments (parent-adjudicated during execution)

- **A-1 (worker supervisor call, 2026-09-15 — decision (a) renderer placement).** Plan defect: the finalize route runs in the **API** host, which references ONLY Core (+ ServiceDefaults/Students/Settings/SchoolCollab cores); `AddAssignmentsCore` is the sole wiring call in Api/Program.cs; `SchoolCollab.Assignments.Application` is a Blazor RCL referenced only by the Admin host. A generator in Application would never resolve in the API's DI container, and Application cannot see Core's `IAssignmentCertificateGenerator`. **Approved Option A:** generator at `src/Assignments/SchoolCollab.Assignments.Api/Services/AssignmentCertificateGenerator.cs`; QuestPDF `PackageReference` (CPM 2026.8.0, no Version) in `SchoolCollab.Assignments.Api.csproj`; registration inline in Api `Program.cs` after `AddAssignmentsCore`. Decision (a)'s intent (QuestPDF OUT of the domain Core project) is preserved. Consequential moves: `AssignmentCertificateGeneratorTests` → `tests/SchoolCollab.Assignments.Api.Tests.Unit/` (Assignments.Tests.Unit doesn't reference the Api assembly); `ModuleServices.cs` drops from the expected-file list; Api `Program.cs` + Api `csproj` enter it. `CertificateDownloadService` stays in Application (Blazor surfaces live there).

## Plan

Execute the C3 slice: certificate PDF generated on finalize, stored via `IFileStore`, referenced from `SignatureEvent.CertificateStoragePath`, downloadable from the teacher Detail Guardian Sign-off tab and the guardian e-sign page.

### Decisions (binding)

- **(a) Package + placement.** CPM gains exactly ONE entry: `<PackageVersion Include="QuestPDF" Version="2026.8.0" />`; `SchoolCollab.Assignments.Application.csproj` gains `<PackageReference Include="QuestPDF" />` (no Version — NU1008 otherwise). The QuestPDF renderer lives in **Application** (`Services/AssignmentCertificateGenerator.cs`) — NOT Core (keeps the rendering dependency out of the domain project). Interface `IAssignmentCertificateGenerator` in **Core/Services** (the `IFileStore` posture), consumed by the Core finalize handler. ModuleServices registers `services.AddTransient<IAssignmentCertificateGenerator, AssignmentCertificateGenerator>()` and sets `QuestPDF.Settings.License = LicenseType.Community;` once (idempotent static — set inside the generator's primary constructor; XML doc records why).
- **(b) Certificate content record.** `Core/Services/AssignmentCertificateContent.cs`: `sealed record AssignmentCertificateContent(string AssignmentTitle, string StudentName, string SignerGuardianName, SignatureType SignatureType, string? TypedSignature, DateTimeOffset SignedAt, DateTimeOffset FinalizedAt, string ConsentTextShown)`. Assembled in the finalize handler from data it now loads: assignment (title), signature event (signer/type/typed name/signed-at/consent), student directory (student + signer-guardian names — degrade to raw ids on a lookup miss, the GetSignOffContext precedent). Layout v1 = ONE A4 page, English, modest (title header; student/guardian/assignment block; signature block showing type + typed name; consent-text footer; page footer with generated-at). No logos, no images.
- **(c) Domain — the single narrow update method (parent-adjudicated deviation, recorded).** `SignatureEvent.AttachCertificate(string certificateStoragePath)`: `ArgumentException` on null/whitespace; **idempotent no-op when already set**; stamps `UpdatedAt`. This is the ONE exception to ar-9's "NO update methods" purity — the column was designed for exactly this; record it in the ar-9 doc's residual line when closing (the residual already says "the C3 certificate round populates it").
- **(d) Finalize handler extension (transactional).** `FinalizeSignOffCommandHandler` gains `IAssignmentRepository`, `ISignatureEvent`-access via the existing `ISubmissionRepository.GetSignatureEventByAssignmentStudentAsync`, `IStudentDirectory`, `ISignatureConsentTextResolver`, `IAssignmentCertificateGenerator`, `IFileStore`. Sequence (single `SaveChangesAsync` at the end — generation failure rolls back the finalize, so a failed finalize is cleanly retryable): load submission → load signature event (**null → `SubmissionSignOffStateException`** — finalize requires a prior sign) → load assignment → resolve names → `submission.FinalizeSignOff()` → build content → `generator.GenerateAsync(content)` → `IFileStore.StoreAsync(pdf, "certificate.pdf", "application/pdf")` → `event.AttachCertificate(path)` → save. Generator/store failures throw typed **`AssignmentCertificateException`** (new `Core/Domain/Exceptions/AssignmentCertificateException.cs`, mirrors `AssignmentApprovalRequiredException` posture) → route maps to **502** (the ar-2 provider-failure precedent).
- **(e) Download route.** `GET /{id}/students/{studentId}/certificate` in `AssignmentRoutes.cs`: load the signature event (404 when none OR `CertificateStoragePath` null) → `IFileStore.OpenReadAsync` (`FileNotFoundException` → 404) → `Results.Stream(stream, "application/pdf", fileDownloadName: $"certificate-{id:N}-{studentId:N}.pdf")`. No `RequireAuthorization` changes; no new flag.
- **(f) ApiClient + download UI (the app's first file download).** `AssignmentsApiClient.GetCertificateAsync(Guid assignmentId, Guid studentId)` → `Task<byte[]>` (`EnsureSuccessStatusCode`, the ar-10 client posture; private wire record if needed). A small JS interop module `wwwroot/js/fileDownload.js` (`window.saveByteArray = (fileName, bytes) => { … blob + anchor click … }`) loaded via `IJSRuntime` dynamic import and cached on the component (`use-js-interop` skill: module reference on the component, dispose in `IAsyncDisposable`, JSDisconnectedException-safe). A thin `Services/CertificateDownloadService` (Application) wraps ApiClient + the JS call. BOTH UI surfaces: `Detail.razor` Guardian Sign-off tab gains a per-ward "Certificate" download action (rendered only when that ward is finalized); `SignOff.razor` gains a download button when the context shows finalized. `_busy`/`_error` reset in `finally` + `StateHasChanged()` in every async action (the ar-8/ar-9 UI-fix class).
- **(g) Out (record — do not touch):** re-notify on finalize (E2's); a regenerate/reissue-certificate action (backlog — v1 generates once on finalize, retry only when finalize itself failed); WS-F3 relocation of the sign page behind real auth + deep links; drawn signature (Phase-5); D-6 identity; `documents/configuration.md` (no new flags); existing migrations (ZERO this round); the pre-round untracked scratch files in `documents/rounds/`; `wwwroot/app.css`; video/transcript extraction.

### Expected files (~19)

**Created (8):**
- `src/Assignments/SchoolCollab.Assignments.Core/Services/IAssignmentCertificateGenerator.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Services/AssignmentCertificateContent.cs`
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/Exceptions/AssignmentCertificateException.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentCertificateGenerator.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/Services/CertificateDownloadService.cs`
- `src/Assignments/SchoolCollab.Assignments.Application/wwwroot/js/fileDownload.js`
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentCertificateGeneratorTests.cs`
- `tests/SchoolCollab.Assignments.Api.Tests.Unit/CertificateRoutesTests.cs`

**Modified (11):**
- `src/Assignments/SchoolCollab.Assignments.Core/Domain/SignatureEvent.cs` (m — `AttachCertificate` only)
- `src/Assignments/SchoolCollab.Assignments.Core/CQRS/Assignments/Commands/SignOff/FinalizeSignOffCommandHandler.cs` (m)
- `src/Assignments/SchoolCollab.Assignments.Application/ModuleServices.cs` (m — generator DI)
- `src/Assignments/SchoolCollab.Assignments.Application/SchoolCollab.Assignments.Application.csproj` (m — QuestPDF ref only)
- `src/Assignments/SchoolCollab.Assignments.Application/Services/AssignmentsApiClient.cs` (m — `GetCertificateAsync`)
- `src/Assignments/SchoolCollab.Assignments.Api/Endpoints/AssignmentRoutes.cs` (m — the GET route)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/Detail.razor` (m)
- `src/Assignments/SchoolCollab.Assignments.Application/Components/Pages/Assignments/SignOff.razor` (m)
- `Directory.Packages.props` (m — the one QuestPDF entry)
- `tests/SchoolCollab.Assignments.Tests.Unit/SignatureEventTests.cs` (m — additive `AttachCertificate` cases)
- `tests/SchoolCollab.Assignments.Tests.Unit/ReassignFinalizeSignOffCommandHandlerTests.cs` (m — additive finalize+certificate cases; worker verifies this file owns the finalize handler tests and reports if the home differs)
- `tests/SchoolCollab.Assignments.Tests.Unit/SignOffPageBunitTests.cs` (m — additive download-button cases)
- `tests/SchoolCollab.Assignments.Tests.Unit/AssignmentDetailBunitTests.cs` (m — additive certificate-action cases)

### Implementation steps (ordered; `dotnet build SchoolCollab.sln` after every step; each step lands build-coherent)

1. **Core seam (decisions (b)+(c)+(d) domain half).** Content record + interface + typed exception + `AttachCertificate` + finalize handler extension (loads event/assignment/names; generation+store calls via the interfaces, still compiling against a not-yet-registered impl is fine at the DI level but NOT at build level — so step 1 ends with the generator interface only, handler wired, build 0 errors because the impl comes in step 2; if Scrutor/compile ordering forces it, land the minimal `AssignmentCertificateGenerator` skeleton in step 1 and fill the layout in step 2). Build; run `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` (existing green + new `AttachCertificate` cases).
2. **QuestPDF renderer + DI + CPM (decision (a)).** CPM entry + csproj ref + `AssignmentCertificateGenerator` (one A4 page, content record → `Document.Create` … `GeneratePdf()` → `byte[]`/`MemoryStream`; license set; `ILogger` structured). ModuleServices registration. Build; new `AssignmentCertificateGeneratorTests` green.
3. **Route + ApiClient (decisions (d) route half + (e)).** `AssignmentRoutes` GET certificate (404 paths + stream) + `GetCertificateAsync`. Build; run `dotnet test tests/SchoolCollab.Assignments.Api.Tests.Unit` (new `CertificateRoutesTests` reusing the `SignOffRoutesTests` harness).
4. **Download UI (decision (f)).** `fileDownload.js` + `CertificateDownloadService` + Detail tab action + SignOff page button. Build; run `dotnet test tests/SchoolCollab.Assignments.Tests.Unit` (bUnit additive cases).
5. **Full matrix + freeze.** `dotnet build SchoolCollab.sln` (0 errors) + the eight test projects (below) + `git add -N -- src tests` + the parent freezes the patch + worker self-report.

### Binding test coverage list (MSTest + FluentAssertions; bUnit + Moq)

- `AssignmentCertificateGeneratorTests` (Assignments.Tests.Unit): `Generates_NonEmptyPdf_WithPdfHeader` (byte[] starts `%PDF`, length > 500), `Renders_BothSignatureTypes` (Click and Typed content both generate without throwing), `LongConsentText_DoesNotThrow` (multi-paragraph consent).
- `SignatureEventTests` (additive): `AttachCertificate_SetsPath`, `AttachCertificate_Idempotent_OnSecondCall`, `AttachCertificate_Throws_OnEmptyPath`.
- Finalize handler (additive, in the file that owns finalize cases — expected `ReassignFinalizeSignOffCommandHandlerTests`): `Finalize_GeneratesAndAttachesCertificate` (mock generator/file-store verify + path attached), `Finalize_Throws_WhenNoSignatureEvent`, `Finalize_RollsBack_WhenGenerationFails` (generator throws → submission NOT finalized).
- `CertificateRoutesTests` (Assignments.Api.Tests.Unit, SignOffRoutesTests harness): `GetCertificate_200_StreamsPdf`, `GetCertificate_404_WhenNoEvent`, `GetCertificate_404_WhenCertificateNotGenerated`.
- `SignOffPageBunitTests` (additive): `DownloadCertificate_Visible_WhenFinalized`, `DownloadCertificate_Hidden_WhenNotFinalized`.
- `AssignmentDetailBunitTests` (additive): `Certificate_Action_Shown_ForFinalizedWard`, `Certificate_Action_Hidden_WhenNotFinalized`.

### Constraints (repo AGENTS.md + rules — restated for the worker)

CPM — no `Version` on any `PackageReference`; this round adds exactly ONE package (QuestPDF 2026.8.0 — do NOT bump past it). net10.0. **No MediatR** — CQRS via `ICommandHandler<T>` / `IQueryHandler<T,R>` + Scrutor. MSTest + FluentAssertions + bUnit + Moq. `dotnet build SchoolCollab.sln` after every change. **No git commits — working tree only.** Primary constructors; XML `<summary>` docs on every new public type/member; structured logging with named placeholders; typed exceptions only (`AssignmentCertificateException` + existing `ArgumentException`/`SubmissionSignOffStateException`) — **no `InvalidOperationException` from new code**. **ZERO migrations** (never touch an existing one; `MigrationGuardTests` must stay green untouched). Blazor rules: FluentUI components; `@key` on every `@foreach`; no `<style>` blocks / no inline `style=`; `EventCallback` for child→parent; `_busy`/`_error` reset in `finally`. JS interop follows the `use-js-interop` skill (dynamic import, module cached + disposed, `JSDisconnectedException`-safe). **The worker NEVER edits `documents/rounds/round-ar-11-c3-certificates.md`** — self-reports go to `documents/rounds/.ar-11-worker-report.md` (scratch, untracked). **30-minute cap + cut-line protocol** (finish the current step build-coherent, report, STOP). **Repo-scoped searches only — NEVER `find /`.** MTP test invocation: `dotnet test tests/SchoolCollab.<Name>` with NO extra flags (`-v q`/`--nologo` make MTP report "Zero tests ran", exit 5).

### Acceptance criteria

Worker-facing:
- `dotnet build SchoolCollab.sln -c Debug`: 0 errors. `dotnet test` on the eight projects: 0 failures (ArchitectureTests untouched-green).
- Changed files = the expected-files list one-for-one (~19, ±1 where a created test folds into an existing file); deviations reported, not silent.

Reviewer-facing (static, diff-only):
- Decisions (a)–(f) implemented as written: QuestPDF in Application (not Core); the single `AttachCertificate` method (no other SignatureEvent mutation); transactional finalize (single save; generation failure rolls back); 502 on `AssignmentCertificateException`; 404 paths on the route; the JS interop follows the skill pattern; both UI surfaces gated on finalized; additive-only test changes.
- Best-practices check: no overwrites; skills honored (`dotnet-best-practices`, `blazor-components`, `use-js-interop`); readability.

Orchestrator-facing (accept verdict):
- Parent-authoritative build + tests green; reviewer verdict PASS (or P2-only triaged); worker self-report reconciled; **P1 list empty → CLOSED**.

### Residual risks / notes for acceptance

- **QuestPDF fonts on Linux/containers:** 2026.8.0 bundles Lato by default — fine for English v1. The 2026.9.0 font-breaking-changes are the reason the pin stays at 2026.8.0.
- **License:** Community tier (org <$1M revenue) — `QuestPDF.Settings.License = LicenseType.Community` recorded in code + here.
- **Generation failure fails the finalize** (strict, transactional): retry = click Finalize again. A decoupled "generate certificate" retry action is backlog (decision (g)).
- **Certificates are never deleted** (IFileStore retention is Phase-5 WS-G); the ar-4 `StagedFileSweeper` does NOT sweep certificate paths — verify the sweeper's reference check excludes them (a review checkpoint, not new work).
- **Names degrade to raw ids** when the student-directory lookup misses (the GetSignOffContext precedent) — recorded, not a defect.
- **No bUnit test executes the real JS save path** (bUnit has no DOM download) — the tests assert visibility/gating + the service call; the JS module is exercised manually/by the tester.

### UI-round note (tester handover — derived by the parent AFTER acceptance)

UI round (Detail.razor, SignOff.razor, first JS interop + download). After the accept verdict the parent derives the tester handover from the changed-file list verbatim. Known-intended behaviors to hand over as "intended, not bugs": the Certificate action renders only for FINALIZED wards; a 404 (missing file) surfaces an error message, not a crash; names on the PDF may show raw ids when the directory misses; finalize failure is retryable by clicking Finalize again.

### Prepared dispatch (parent)

1. ✅ ~~Merge #232~~ — **SKIPPED by owner instruction ("don't merge yet")**; branch cut from stack/10 tip `a5f49efd` instead (recorded above).
2. ✅ Branch `stack/11-ar-11-c3-certificates` @ `a5f49efd`, registered in the 2-layer train via the documented gh-stack dance (`main ← stack/10 #232 ← stack/11`).
3. ✅ Execution-mode menu → owner chose **light round** (Tiers 1–2).
4. ✅ Worker task composed from the plan + A-1 amendment; dispatched 2026-09-15 09:30Z.

## Worker Report

Worker: `worker` agent on `ollama/deepseek-v4-flash:0731-cloud`, pass 1 (run `92eb6877`), 2026-09-15 09:30–09:51Z. One supervisor decision (A-1 renderer placement, parent-approved mid-pass). Self-report also at `documents/rounds/.ar-11-worker-report.md`.

```text
WORKER REPORT
Changed files: 8 created + 15 modified (23; patch = diffs-ar-11-c3-certificates.patch, 1,436 lines)
Created: Core/Services/IAssignmentCertificateGenerator.cs; Core/Services/AssignmentCertificateContent.cs;
  Core/Domain/Exceptions/AssignmentCertificateException.cs; Api/Services/AssignmentCertificateGenerator.cs (A-1);
  Application/Services/CertificateDownloadService.cs; Application/wwwroot/js/fileDownload.js;
  Api.Tests.Unit/AssignmentCertificateGeneratorTests.cs (A-1); Api.Tests.Unit/CertificateRoutesTests.cs
Modified: Directory.Packages.props; Api.csproj (A-1); Api/Program.cs (A-1); AssignmentRoutes.cs; SignatureEvent.cs;
  FinalizeSignOffCommandHandler.cs; AssignmentsApiClient.cs; ModuleServices.cs (CertificateDownloadService only);
  SignOffSection.razor (deviation 3); SignOff.razor; SignatureEventTests.cs; ReassignFinalizeSignOffCommandHandlerTests.cs;
  SignOffPageBunitTests.cs; AssignmentDetailBunitTests.cs; SignOffSectionBunitTests.cs (deviation 4, new file)
NOT touched (pre-round carryover): impl-details.md, breakdown.md, AssignmentCreateBunitTests.cs
Build: 0 errors (dotnet build SchoolCollab.sln); pre-existing warnings only, none from new code
Deviations: (1) A-1 parent-approved placement; (2) ModuleServices only for CertificateDownloadService;
  (3) Detail.razor untouched — per-ward Certificate action lives in SignOffSection.razor (Detail hosts it);
  (4) SignOffSectionBunitTests.cs added (DI registration for CertificateDownloadService)
```

## Review

Reviewer: static diff-only on `ollama/kimi-k2.7-code:cloud` (run `553e8caf`), 2026-09-15 09:53–09:56Z, patch `diffs-ar-11-c3-certificates.patch` + plan (incl. A-1) + worker self-report + repo skills (dotnet-best-practices, blazor-components/css-isolation, fluentui-*, use-js-interop).

```text
REVIEW
Verdict: PASS
P1: none
P2: none
Best-practices: no overwrites / skills honored / readable

Notes:
- Amendment A-1 placement honored as binding.
- Detail.razor deviation adjudicated acceptable: SignOffSection renders inside Detail's Guardian
  Sign-off tab; per-ward Certificate action gated on `row.FinalizedAt is not null`; SignOff.razor
  download button gated on `_context.FinalizedAt is not null`.
- SignOffSectionBunitTests.cs only adds DI registration for CertificateDownloadService; no tests removed.
- Single AttachCertificate mutation, single SaveChangesAsync, 502 mapping, 404 paths,
  dynamic-import + cached-module + IAsyncDisposable + JSDisconnectedException-safe interop,
  CPM pin 2026.8.0 with no PackageReference Version, zero migrations — all verified.
```

Parent spot-check confirmed the two gating claims in the tree (Detail.razor:327; SignOffSection.razor:94–100).

## Acceptance

**Verdict: CLOSED — P1 list empty.**

- [x] Build (parent-authoritative): `dotnet build SchoolCollab.sln` — **0 errors**.
- [x] Tests (parent-authoritative, MTP no-flags): Core 79/0 · **Assignments 576/0 (+10)** · **Assignments.Api 52/0 (+38)** · Students 422/0 · Students.Api 1/0 · Settings 519/0 · Settings.Api 1/0 · **ArchitectureTests 20/0** — total **1,670 / 0 failures**. MigrationGuard green untouched.
- [x] Scope check: patch = 23 files, one-for-one vs the A-1-amended expected list; carryover files excluded from the patch and untouched by the worker.
- [x] Decisions (a)–(g) as amended (A-1) implemented; reviewer verdict PASS, P1/P2 empty; both worker deviations (3)(4) adjudicated acceptable.
- [x] Loop bounds: 1 worker pass, 1 reviewer pass, 0 rework iterations (≤1 Tier-2 bound).

Residuals / notes:
- **UI tester skipped** (light round, owner choice). The surfaces (Detail.razor Guardian Sign-off tab, SignOffSection.razor, SignOff.razor, first JS interop `fileDownload.js`) have NOT had the adversarial tester pass; the tester handover in the UI-round note below is parked and can run on request.
- Certificates are never deleted (IFileStore retention is Phase-5 WS-G) — recorded, accepted.
- Names degrade to raw ids on directory miss (GetSignOffContext precedent) — recorded, accepted.
- QuestPDF pinned 2026.8.0; do not bump past it without owner instruction (2026.9.0 font breaking-changes).

## Post-acceptance CI fix (2026-09-15 — same branch, follow-up commit)

`#233`'s first CI run failed on `AssignmentCreateBunitTests.Create_AuthorOverrides_OverridesPrefillAndSubmitsValue`
— the **same CI-only flake** first seen on #232's CI. The diagnostic wrapper added to that test (which rode
this branch's commit) made the root cause visible: `Page _error: Please select a subject.` — i.e. the test's
reflection-primed `_selectedSubject` was **null at submit**.

**Root cause (test-side, product correct):** `Create.razor` has two `@bind-*:after` handlers that clear
`_selectedSubject` for the FR-58 re-filter — line 234 `@bind-SelectedValues:after="OnSelectedGroupsChangedAsync"`
(unconditional clear) and line 279 `@bind-Value:after="OnDueDateChangedAsync"`. FluentUI raises those callbacks
**one render pass late**; on loaded CI runners the pass lands after the test's reflection write, so `SubmitAsync`
reads null and bails at the subject guard. Locally the queue drained first (~55 clean runs; CI hit it twice in
~3 runs).

**Fix (test-only, one hunk):** the priming write now happens **inside the same `cut.InvokeAsync` delegate as the
`SubmitAsync` invocation**, so no queued binding cascade can interleave write and read. The diagnostic wrapper is
retained (it is what made this diagnosable).

**Verification:** local 576/0; Linux Release + CI env in the container **0/12 failed**; interleaving window
eliminated by construction. Captured as project skill `fix-flaky-bunit-fluentui-after-cascade`.

## Execution provenance

| Role | Model | Run | Outcome |
|---|---|---|---|
| Parent (orchestrator, Tier 2) | session | — | plan + A-1 + acceptance |
| Worker pass 1 | `ollama/deepseek-v4-flash:0731-cloud` | `92eb6877` | 23 files, build 0 errors, 1 supervisor call (A-1) |
| Reviewer 1 (static) | `ollama/kimi-k2.7-code:cloud` | `553e8caf` | PASS, P1 none, P2 none |
| UI tester | — | — | skipped (light round, owner choice) |