# Agent lessons (durable)

Lessons accepted by the owner after a build or defect pass. **Newest first.** Each entry carries:

- **the date** it was accepted,
- **the rule** — imperative, in this repo's voice,
- **the evidence** — what actually happened, with the command or error that showed it,
- **the trigger** — the signal a future agent should recognise as "this lesson applies".

`AGENTS.md` and the specialty rules **point here; they never restate a lesson inline.** A lesson
earns a place here only after the owner accepts it (`AGENTS.md` § "Propose, never pre-write"). A
lesson that fires twice should be promoted into a deterministic check or a specialty rule, and the
entry here shrinks to a pointer at that check.

---

## A model registry row is not authoritative for a model's kind or capability (2026-10-30)

**Rule.** Before building a plan on what a model *is* or *can take*, read the vendor's own page for that
**tag** — not `pi --list-models`, not a catalog index, not the provider block of a local store. Registry rows
describe what the harness will resolve, and they are frequently silent about kind: classifier models are not
chat models and do not appear in `/model` at all. The vendor page is also the only place the request shape,
the hard input cap and the billing unit are stated, and each of those changes the design.

**Evidence.** Spec `documents/specs/decision-model-seat.md` was first drafted on a chat-model premise: "no
thinking support, no image input, 128K context". `pi --list-models` printed
`ollama clef:27b-cloud 128K 16.4K no no` — wrong on context (the tag page says **64K**, and cloud input is
**≤65,536 tokens, rejected not truncated**) and wrong on images (the model is multimodal, ≤4 PNG/JPEG/WebP per
request). More damaging than either number: the model **does not chat** — it is a non-autoregressive classifier
on `POST /v1/systemone` that scores a schema of typed questions in one pass, so the citation-based verification
the draft was built around was impossible. The owner caught the image error from the library page; it cost a full
rewrite of the spec's mechanism sections.

**Trigger.** You are writing anything that depends on a model's capability — context, image input, tool use,
output shape, price — or on how the harness reaches it. Also whenever a plan's verification step says "the model
cites / lists / reports line numbers": classifiers score, they do not cite.

---

## A classifier's `confidence` is not its top probability, and a route can drop images in silence (2026-10-30)

**Rule.** Two separate traps, one habit: **name the quantity and name the route.** When thresholding a classifier
answer, threshold the **max probability** of the answer and say so — a returned `confidence` is a different,
more conservative number (the vendor's own example: top option `0.8425` with `confidence` `0.6298`). And when the
capability under discussion is image input, the claim is never "the model takes images": it is "this model **on
this route** takes images", because at least two routes accept the request, return a plausible answer, and discard
the images — OpenRouter (documented: answers in TypeSafe's format without an error, dropping images) and the
TypeSafe Python SDK against Ollama (documented: uploads images in a format Ollama does not accept). Both failures
are invisible in the output.

**Evidence.** Mining `https://ollama.com/library/clef:27b-cloud` for the seat spec: the API example returns
`"choice": "meals", "probabilities": {"meals": 0.8425, …}, "confidence": 0.6298`, which makes the draft's
"threshold `confidence` ≥ 0.80" reject a row whose top option is 84% likely — the threshold was unusable as
written. The same page's TypeSafe SDK section ends "To send images, use the API or the Ollama CLI. The SDK uploads
images in a format Ollama doesn't accept", and `docs/models.md` records OpenRouter dropping them without an error.

**Trigger.** You are writing a threshold, a confidence gate, or a "high-confidence only" filter over model output;
or you are choosing between an SDK, a proxy, a gateway and the vendor's own endpoint for a multimodal request.


**Rule.** Before projecting a field into a new DTO, ask which context **owns** that field and what its own read
already enforces. A new read that surfaces another module's protected data is not a display decision — it is a
permission decision taken by whoever wrote the projection. Prefer **omitting the field from the contract** to
masking it in the view: a value that never enters the payload cannot leak through devtools, a screenshot, a log
line or a future export, and a mask is cosmetic control that one DOM inspection undoes.

**Evidence.** The assignment view spec proposed an audience table showing each student's guardians and contacts.
The mock rendered masked values (`n.okafor@…`, `+44 7700 900…`) and the owner stopped it: a local-part plus domain
and a partial phone number still identify a household, and masking is not a control. The values would have come from
`NotificationLog.ToAddress` and the Students contact read — data the Students module already guards with its own
scope. The replacement rule ("show a guardian name only if the caller could already list that student's guardians in
the Students module") then turned out to have **no mechanism**: `TeacherScope` answers "can you see this
assignment", not "may you list this student's guardians", and a per-student authorization hop would fan a single
read into N hops. Dropping names entirely was both the safest and the simplest answer.

**Trigger.** You are adding a field to a list read and the field belongs to another context — contact details,
guardian identity, addresses, dates of birth, medical or SEN flags. Also when the plan says "masked" or "shown only
to authorised users": name the check that already exists, or drop the field.

---

## A recursive grep over a build-output tree reads as a hang, and a pipe is not an exclusion (2026-10-30)

**Rule.** Search source with `grep -rn --include=*.cs --include=*.razor --exclude-dir=bin --exclude-dir=obj <pattern> <path>`.
`--exclude-dir` is the flag that prevents the walk; `| grep -v "/bin/\|/obj/"` does **not** — the filter runs after
the walk has already read every DLL. When you already know the file, open it. On a repo this size an unfiltered
recursive grep takes minutes, produces no output, and looks exactly like a deadlocked command to any watchdog.

**Evidence.** Two worker runs stalled on this in one session. The second wrote
`grep -rln audience tests/SchoolCollab.Assignments.Api.Tests.Unit | grep -v "/bin/\|/obj/"` — the pipe *looks* like the
exclusion, so the steer that stated the rule an hour earlier did not prevent it. The parent found the pipeline via
`Get-CimInstance Win32_Process -Filter "Name='grep.exe'"`, saw the `-v` matcher as a second process waiting on the
first, killed it, and the child resumed normally. CPU sampling was the tell: 15 idle `dotnet` node-reuse daemons sat
at a **zero** CPU delta, so the stall was not a build.

**Trigger.** Any tool call still running past ~2 minutes on `bash`/`shell` whose command contains `grep`, `rg`, `find`
or a glob over `src`/`tests`. Kill the walk, not the run, and re-issue with `--exclude-dir`.

---

## A round patch is a diff against the stack base, never against `HEAD` (2026-10-30)

**Rule.** Generate `documents/rounds/diffs-<slug>.patch` with `git diff <base>..HEAD -- src tests`, where `<base>` is
the branch point (or a stack layer's parent). `git diff HEAD` on a committed tree is **empty**, and an empty patch
file commits silently and looks like a legitimate artifact.

**Evidence.** The round's patch was re-stamped after the work had been committed, using `git add -N` + `git diff
HEAD`; it came out at **0 lines / 0 files** and was nearly committed that way. Recomputing against `main` produced
the real artifact: 7,163 lines across 32 files. A second trap sits alongside it: `git diff 41e9cc7f..HEAD` with the
space renders a *range* of commits, so a path-limited diff must be written `41e9cc7f..HEAD -- src tests`.

**Trigger.** Any time you write or refresh a `.patch` artifact, and every time you check a round's expected-files
list — verify the file count against the plan before calling the freeze done.

---

## Killing "every `dotnet` except the AppHost root" kills the application (2026-10-30)

**Rule.** An Aspire AppHost's services are ordinary `dotnet run --project src/<Service>` children. To stop the
application, kill the **whole tree** (`taskkill /PID <root> /T /F`) and confirm the service ports closed; to free
memory, do not selectively kill children by process name — the root keeps running, reports healthy, and every
request times out because nothing is listening behind its proxy.

**Evidence.** Hunting `ERR_INSUFFICIENT_RESOURCES` (machine at **0.2 GB free** with 26 `dotnet` processes), the
parent kept the AppHost root and killed 25 others it had classified as "build". Those were assignments-api,
students-api, settings-api, the AI worker and two Blazor hosts. The AppHost still logged "Distributed application
started"; `curl` to the admin port then returned `000` three times in a row, and Playwright reported
`net::ERR_TIMED_OUT`. The classifier was the bug: a process whose command line reads
`dotnet run --project …` is a service, not a build.

**Trigger.** Freeing memory or "cleaning up" while an AppHost is up; a page that suddenly stops responding while the
dashboard still shows the app as running; `ERR_INSUFFICIENT_RESOURCES` / `ERR_TIMED_OUT` on a port that answered a
minute ago.

---

## A newly registered model is invisible to subagents until pi restarts (2026-10-30)

**Rule.** `/reload` refreshes the **session** model list only. The subagent extension keeps its own registry, loaded
when the session starts, and rejects a newly added model under every id form (`provider/id`, bare `id`, alternate
provider). A pi restart is what rebuilds that registry — do not plan a child run around a model registered minutes
ago. For what the model *is* or *can take*, this entry says nothing: see "A model registry row is not authoritative
for a model's kind or capability" above, which is the entry to trust and the one to keep current.

**Evidence.** After the owner's `/reload`, `ollama/clef:27b-cloud` appeared in `pi --list-models`, yet dispatching it
as `ollama-cloud/clef:27b-cloud`, `ollama/clef:27b-cloud` and bare `clef:27b-cloud` all returned "Unknown subagent
model … in the active Pi model registry". The probe fell back to the already-proven worker model, and the fidelity
verdict stayed with the parent seat — which was the right split regardless, because a probe driver only needs to
collect evidence, not judge it.

**Trigger.** A model works in the session but a child refuses it; a registry grep showing an id that `--list-models`
will not print; any plan whose first step is "dispatch to the model we just added".

---

## Never round-trip a UTF-8 file through `Get-Content` / `Set-Content` (2026-10-29)

**Rule.** On this platform, `Get-Content` decodes UTF-8 as the system ANSI code page and `Set-Content -Encoding
utf8`
writes that mis-decoded text back as UTF-8, **doubly encoding every non-ASCII character** — silently, with exit
code 0. Use the editor tool for in-place edits, or open and write with an explicit encoding on both sides, and then
**check non-ASCII output** before treating the edit as done.

**Evidence.** A one-line PowerShell replace on the mock generator (`(Get-Content f) -replace … | Set-Content
-Encoding utf8`) corrupted **289 sequences** in `build-mock.js` — every em dash, quote, box-drawing character and
arrow in comments and copy. Nothing failed: node ran, the build succeeded, and the generated HTML was valid. It
surfaced only when a rendered PNG showed `Published â€” the view page` and a mangled `▾` chevron. Repair needed six
explicit byte-sequence mappings rather than a blanket re-decode, because later editor writes had added correctly
encoded characters to the same file — a naive `latin1 → utf8` round-trip would have broken those instead.

**Trigger.** Any scripted in-place edit of a file that may contain non-ASCII: `.ps1`, `Get-Content`/`Set-Content`
pipelines, `Out-File`, or a shell redirection. After the edit, grep or render for `â€`, `Â`, `Ã` — and look at one
rendered output, because tests will not catch an encoding defect in a string that only a human reads.

---

## A script that has never run is not a deliverable (2026-10-28)

**Rule.** A seed, fixture, migration or one-off helper script is delivered only once it has **executed against a
real database** and its own self-check agreed with what the documentation claims. Writing it is not delivering
it: a script is the one artifact where "it looks right" and "it runs" have no relationship at all — nothing in
the build compiles it, no test imports it, and it is usually the only code in the change that nothing else ever
calls.

**Evidence.** L2b shipped `student-view-probe-seed.{students,assignments}.sql` — a documented expected-count
table, a self-checking fold, and two halves spanning two databases — and the implementer never ran either half.
The first execution failed on its first insert: `null value in column "gender_coded_value_id" of relation
"students" violates not-null constraint` (the column is NOT NULL per migration
`20260716183515_RequireStudentDobAndGender`). The second, once it ran, printed `review_required 3` where its own
runbook documented `2`, because the "awaiting submission" fixture carried `mandatory_review = true` and doubled
as a second review fixture. Both defects survived four review seats — plan gate, worker, static reviewer,
acceptance — and both surfaced inside a minute of execution.

**Trigger.** You are about to hand back, accept, or merge any `.sql`, `.ps1`, `.sh` or similar one-off script —
especially a dev-only probe seed, where "dev-only" is exactly why nobody thought to run it. The cheap proof is
one execution against the target database with its self-check output pasted into the round doc. If it cannot run
in this environment, that is a finding to report, never a reason to accept it.

---

## A correction is not finished until every copy of the old value is gone (2026-10-28)

**Rule.** When a review finding corrects a **value** — a route, a signature, a file path, a claim about another
assembly — grep the round doc for the old value and fix **every** occurrence before re-submitting. A round doc
states the same fact in four places (the plan's prose, the expected-files table, the criteria, the test rows),
and the table is the one a worker implements from; the criteria are not.

**Evidence.** L2b's plan gate (iteration 1) found that the client's wire path omitted the `/assignments`
segment. The amendment fixed C8 to `assignments/{studentId}/assignment-stats` and fixed the test row — and left
the **implementation row** saying `{studentId}/assignment-stats`. Iteration 2 returned NO-GO on exactly that
contradiction: *"the row the worker implements from still says the wrong wire path"*. The residue was not
cosmetic — the panel would have rendered "unavailable" against a **healthy** endpoint, and C8's own test would
have encoded the wrong path, so every test would have agreed with the bug. `grep -n 'assignment-stats' <round
 doc>` after the amendment shows both strings side by side in one screen.

**Trigger.** Any amendment after a gate NO-GO, a reviewer's `file:line` correction, or your own mid-plan
change of mind. Before re-submitting, search the document for the value you just changed — a second hit that
did not move is the defect. Distinct from *"A claim about the codebase is only as true as the search that
produced it"*: that entry governs **making** a claim, this one governs **updating** one.

**Why this is not promoted into a check.** It fired twice on the day it was written — a plan amendment that
fixed a wire path in three places and left the fourth, then a corrected `NOT NULL` note sitting directly above
its own stale "it is nullable in the schema" comment. Both times the catch was a **seat** (plan gate iteration
2, then the acceptance seat), not a mechanism, and both times it landed before merge. The defect is semantic: no
grep distinguishes a stale sentence from a deliberately quoted old value, and round docs legitimately quote old
paths inside their gate transcripts. **The check is a seat, not a grep — do not "promote" this into a test that
only matches wording.**

---

## A source-string test can pass on a comment (2026-10-28)

**Rule.** An assertion of the form "this `.razor` contains X" must match **markup** — a tag, an attribute, a
line that starts the construct, an action list — never a phrase a human would also write. A `@code` comment,
a `<see cref="…"/>` doc reference and a rendered label are indistinguishable to `String.Contains`, so a test
anchored on prose keeps passing after the markup it was written to protect has been deleted.

**Evidence.** Two `StudentDetailSectionsTests` assertions were green for the wrong reason, and the student-view
redesign is what exposed them. `Detail_ActivityGroups_Has_JoinGroup_Button` asserted
`source.Should().Contain("Join Group")` and matched a `@code` comment — *"Join Group opens JoinGroupsDialog"* —
so the button could have been renamed or removed and the test would still have gone green.
`Contacts_Section_IsRemoved_GuardiansGridCarriesContacts` asserted `NotContain("<StudentGuardiansList")` and
was satisfied by a doc comment, `<StudentGuardiansList> component is a thin presenter…`, even while a real
`<StudentGuardiansList …>` element sat in the markup. Both now assert the rendered mechanism: the panel's
action list (`GroupPanelActions` → `OnJoinGroupAsync`) and the element matcher
`<StudentGuardiansList\s+[A-Z]`, which a `<see cref>` cannot satisfy.

**Trigger.** Writing — or trusting — a `Should().Contain(...)` / `Should().NotContain(...)` whose expected
text reads like English: a label, a sentence, a component name in prose. If the expected string would look at
home in a comment, the assertion is about the comment.

---

## A claim about the codebase is only as true as the search that produced it (2026-10-27)

**Rule.** Before writing any claim of the form "there is no X" **or** "every X does Y", search for the
**shape** of X across **every** bounded context and every naming convention the repo actually uses — then
scope the sentence to the search you ran. Never assert absence *or* universality from a grep over the
obvious project, seeded with identifiers you invented. If the search is not written next to the claim, the
claim is a guess wearing a fact's clothes.

**Evidence.** The agent wrote *"there is no student-numbering policy or generator in `src/Students`, and
every `Sequence` belongs to `Period`"* after grepping that one directory for `GenerateStudentNumber`,
`NextStudentNumber`, `numbering` and `Sequence`. The feature existed: `IEntityCodeGenerator` (contract in
`SchoolCollab.Core/EntityCodes`), implemented by `EntityCodeGenerator` and `EntityCodeRule.GenerateNext`
in `SchoolCollab.Settings.Core`, called from `CreateStudentHandler.cs:31` as `GenerateAsync("STUDENT_CODE")`,
seeded at `EntityCodeRuleSeeder.cs:30`, with its own tests and design doc. The word is **"entity code"**,
not "student number" — the search used a student-shaped name inside the student module, a place the
feature was never going to be. Both halves of the claim were *narrowly true* and *operationally false*,
which is why repeating the same grep confirmed it.

**Trigger.** A sentence containing "no", "none", "does not exist", "missing", "every", "all … are" or
"only" about this codebase. Same family as the retraction lesson below: enumerate the shapes, not the token.

---

## Retract a claim by enumerating its shapes, never by grepping the wording you remember (2026-10-27)

**Rule.** When withdrawing a false statement that appeared in several places: enumerate **every wording
variant** first (one grep over a candidate list, including the short forms), fix each, re-run the
enumeration, and record the count beside the fix. A grep of the phrase you happen to recall is not a
retraction; it is a fix of one copy.

**Evidence.** The false claim "the acceptance seat is model-independent of worker **and** reviewer" was
corrected in `SKILL.md` and `references/models.md`; the agent verified with a grep for
`"independent of worker and reviewer"` and called it clean. It had missed **"acceptance is independent of
both"**, which survived one line beneath a corrected twin and left the file contradicting itself. The
higher-model document review found it on its first pass — and also that the recorded reason said "four
places" when the true count was six. Related but distinct from the half-converted-gate lesson above: that
one is about code residue after a flag retirement, this one about prose variants after a retraction.

**Trigger.** Any change whose purpose is to **remove** a statement. Check: one grep over a variant list
including short forms ("of both", "both"), and the count recorded next to the fix.

---

## A half-converted gate is a capability loss (2026-10-26)

**Promoted to `documents/solution/feature-flag-workflow.md` § "Retiring a flag".** Removing a flag means
*converting every consumer* — grep the residue shape (`if (false)`, an always-firing `return;`, a `bool`
nothing assigns `true`), never the deleted key — and assert the **rendered consequence**, not the request.
That doc is read during a retirement, when the rule must fire. Evidence: PR #420 — `Subjects.razor` kept
`if (false)` and the Activity Group owner filter silently vanished, hidden by a guard test that asserted
only the HTTP call.

---
## An over-broad assertion reports itself as timing (2026-10-26)

**Promoted to `.github/copilot/rules/testing.md` § "A render-time exception looks like a FLAKY TIMEOUT"**
as its second cause: `Single`/`First`/`Count` over a shared request log is a **session** claim, not an
endpoint claim — filter to the endpoint and put the observed values in the failure message. Evidence: PR
#420/#418 (`NotificationTemplateCreateTests` reddened two of three full-suite runs with
`Sequence contains more than one matching element`). Five latent sibling sites remain unfixed:
`EnrollmentExceptionsDialogTests.cs:648, 796, 853, 944, 975`.

---
## Kill the repo's own processes yourself — do not ask (2026-10-24)

**Rule.** A `MSB3021` / `MSB3027` lock on a solution build is yours to clear. The locked outputs
belong to the repo's own services, and stopping one destroys none of the user's work:

```powershell
Get-Process | Where-Object { $_.ProcessName -like 'SchoolCollab*' } | Stop-Process -Force
```

A `dotnet watch` or the AppHost also needs its `dotnet` parent killed (`taskkill /PID <pid> /T /F`),
or it respawns the children within seconds. **One exception:** an IDE-launched debugger session is
the user's own session — ask before closing that. Kill, rebuild once, and report only a *surviving*
lock. Never retry the build in a loop and never wait on a question you can answer with a process list.

**Evidence.** 2026-10-24, the #404 round: three solution builds died on
`MSB3027 … The file is locked by: "SchoolCollab.Auth (31876)"` /
`"SchoolCollab.AI.Server (36632)"` / `"SchoolCollab.Assignments.Worker (33196)"` across roughly
twenty minutes. The first two rounds of that were spent surfacing the lock and waiting for an
instruction; the third was spent learning that killing the `dotnet watch` parent was necessary
because its children respawned. `AGENTS.md` at the time said *"surface the lock to the user and let
them choose how to stop the offending process"* — the wrong default for processes the agent owns.

**Trigger.** A build fails with `MSB3021`/`MSB3027`, or you are about to write "the build is blocked
by a running service — should I stop it?".

---

## A test run's summary is not its verdict (2026-10-24)

**Rule.** Read the **exit code** and the `Zero tests ran` lines, never the `failed:` counter alone.
`Directory.Build.props` injects `--ignore-exit-code 8` into every test project, so a filter that
matched nothing exits 0 and prints `failed: 0`; the same tolerance makes the summary lie in the
other direction when only *some* assemblies match nothing. `scripts/round/gates.sh` reads both on
purpose and treats "zero tests ran" as a failure — let it, or replicate that check.

**Evidence.** 2026-10-24, twice in one session. First: a targeted `--filter` run passed while CI
failed, because the filter could not see the consumer it had broken (a test asserting
`#authoring-basics .panel-section__title`, a header the change had just removed). Second, and the
sharper one: a local Release run printed `failed: 0 / succeeded: 4522` and **still exited 8** with
`Failed!`, because five assemblies ran zero tests — the exact reading that "closed nothing" on
2026-10-12 and again on 2026-10-15 (`scripts/round/README.md`).

**Trigger.** Any `dotnet test` you read yourself, especially one whose numbers you are about to
quote as proof; and any change to a shared component, where a filter cannot see the consumers.

---

## The MAUI head is not in the solution, so a shared-component break is invisible (2026-10-16)

**Rule.** A change to `SchoolCollab.Admin.Shared` or to any package version must also build the
mobile head —
`dotnet build src/Mobile/SchoolCollab.Mobile/SchoolCollab.Mobile.csproj -f net10.0-windows10.0.19041.0 -m:1 -p:BuildInParallel=false`
— because `src/Mobile/SchoolCollab.Mobile` is deliberately absent from `SchoolCollab.slnx`. The
`-m:1 -p:BuildInParallel=false` flags are part of the rule: the parallel build exhausts memory on
this machine and kills the agent process instead of reporting a compile error. `gates.sh` runs this
leg by default; `--no-maui` is only ever a baseline flag.

**Evidence.** 2026-10-16, the FluentUI Blazor 4.14.2 → 5.0.1 bump: the main solution stayed green
while the mobile head carried 22 v5-invalid call sites. Nothing in the primary build could see them.

**Trigger.** Touching `Admin.Shared`, any `Directory.Packages.props` pin, or writing a PR that
claims a shared component is covered.

---

## Hot reload cannot apply rude edits — a green build is not a changed page (2026-10-12)

**Rule.** When the tests pass but the running preview "doesn't work", **restart the preview
(stop/start) before debugging the code.** Hot reload refuses "rude edits": a new component
`[Parameter]`, a changed record's shape, new `@implements`/`@inject`, or a **new static asset**
(a collocated `*.razor.js`, served from `_content/...` only after a rebuild). bUnit's loose JS mode
hides the missing-asset 404, so nothing in the test suite contradicts the blank page.

**Evidence.** 2026-10-12, the recorder-pill round: tests green, preview missing the pill entirely.
The asset was a new collocated module that the running circuit had never loaded. Restarting
reproduced a working page with no code change. Symptom set:
`documents/runbooks/dev-loop-hot-reload.md`.

**Trigger.** "Tests pass but the page is blank / the new control is absent", especially right after
adding a `[Parameter]`, changing a DTO's shape, or adding a collocated JS module.

---

## A `(W)` deny refuses reads, and a folder deny breaks the skill tooling (2026-10-05)

**Rule.** Lock a vendored skill file with the write-only mask `(WD,AD)` on the **file**. Never a bare
`(W)` — `icacls` renders it *Write, Synchronize*, and almost every open requests Synchronize, so it
refuses reads as well as writes. Never a deny on the folder: it breaks the skill tooling outright.

**Evidence.** With `(W)` in place, `.NET OpenRead`, `OpenText`, `Get-Content -ErrorAction Stop` and
`cmd /c type` all failed with *Access denied*, while an undenyed skill (`gh-stack`) read fine —
the lock had made the skill unusable, not just unwritable. A folder-level `(W,D,DC)` deny was
removed the same day after `skill_manage create` began reporting `EPERM … scandir
'<…>/<skill-folder>'`; the failing `create` was never retried, so the folder deny is the
strongly-indicated cause rather than a proven one. `(WD,AD)` was then verified to allow
`OpenRead`/`OpenText` while still refusing `OpenWrite` and `Add-Content`.

**Trigger.** About to run `icacls … /deny` on anything, or a skill file that reads back as
"Access denied" and looks corrupt.

---

## Never put a multi-minute wait in a subagent brief (2026-09-25)

**Rule.** CI polling, sleeps and any other multi-minute wait belong in the **parent**, which
survives an interrupted call or a compaction. Hand a delegate only the quick legs (stage/commit/
push/PR, or merge + pull). When a brief is cut short anyway, recover from live state — `git log`,
`git ls-remote`, `gh pr view`, `gh pr checks` — and resume from the first unfinished step; never
re-run the whole brief.

**Evidence.** 2026-09-25: a delegate was aborted mid-poll by a session compaction. Only the steps it
had already completed — commit, push, PR — had persisted; the poll it was sitting in was lost, and
the round had to be reconstructed from the remote rather than from the agent.

**Trigger.** Writing a brief that contains "wait for CI", a `sleep`, or a poll loop.
