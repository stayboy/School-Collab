# Round: decision-model-spike — does the classifier seat work on our artifacts?

**Status:** `CLOSED` (spike) — not committed with the probe's scripts, which stay under `.pi/tmp/`
(gitignored). **Mode:** solo (Tier 0, self-run review and self-acceptance; owner Q21, 2026-10-30).
Branch: `docs/decision-model-seat` @ `01b3c7e0`. Base: `4338b9ce`.
Route executed: **local Ollama 0.40.8 → ollama.com** (`clef:27b-cloud`; the API echoed
`"model":"clef:27b"`). Spec: `documents/specs/decision-model-seat.md`. Decisions: spec §9 Q14–Q24.

## Plan

Prove or refute, for cents and in one sitting, the three claims the seat's design rests on — before
spending a protected-file review on it. Bars set by the owner in Q22:

| # | Criterion | Bar |
|---|---|---|
| A1 | Type spelling resolves on the wired route | one spelling returns 200 and the other is rejected — the value must be **known**, not assumed |
| A2 | Atom-level text claims on a real artifact | **≥12/13** correct **and** at/above the §6.3 threshold |
| A3 | Known-absent controls | **3/3** — catches a model that answers "PRESENT" to everything |
| A4 | One image pass on a real screenshot, hand-read first | answers match the hand-read |
| A5 | The record names the **route** | §5.4 — "supported" is a model+route pair |

Explicitly out of scope: adoption (§8 files), any `/src` change, any test-suite target, the mock-vs-portal
comparison itself.

## Worker Report (self-run)

**A1 — type spelling.** `noul` → `200`, `{"noul":0.9858}`, 147 input tokens. `bool` → **`400`**:
`question "says_hello": type must be choice, noul, or score`. pi's classifier doc example (`type: "bool"`)
is **wrong for this route**. Spec §5.3 updated from "probe it" to "settled: `noul`".

**A2/A3 — text pass.** State = the whole mock (`documents/specs/assets/assignment-view/assignment-view-v1.html`,
45,590 bytes → **16,774 input tokens**). 10 true claims + 3 absent controls, each a 4-option `choice`
(`PRESENT | PARTIAL | ABSENT | UNVERIFIABLE`).

| id | truth | answer | max prob | confidence |
|---|---|---|---|---|
| c01 Delivery "Contacts" column | PRESENT | PRESENT | 0.9214 | 0.8031 |
| c02 Delivery "Guardians" column | PRESENT | PRESENT | 0.9600 | 0.8964 |
| c03 Progress "Sign-off" column | PRESENT | PRESENT | 0.9592 | 0.8943 |
| c04 Progress "Last activity" column | PRESENT | PRESENT | 0.9438 | 0.8560 |
| c05 stat line "28 students" | PRESENT | PRESENT | 0.9507 | 0.8730 |
| c06 "not reached" delivery state | PRESENT | PRESENT | 0.9368 | 0.8390 |
| c07 "Rules & policy" panel | PRESENT | PRESENT | 0.9690 | 0.9191 |
| c08 "one row per student in the cohort" | PRESENT | PRESENT | 0.8952 | 0.7404 |
| c09 "Status" panel | PRESENT | PRESENT | 0.9317 | 0.8262 |
| c10 Questions "Type" column | PRESENT | PRESENT | 0.9597 | 0.8954 |
| x11 **control** Delivery "Phone" column | ABSENT | ABSENT | 0.9616 | 0.9004 |
| x12 **control** "Attendance" panel | ABSENT | ABSENT | 0.9672 | 0.9145 |
| x13 **control** Progress "Behaviour" column | ABSENT | ABSENT | 0.9648 | 0.9083 |

**13/13 correct · 3/3 controls · 13/13 above the 0.80 threshold · 1,468 ms · 16,774 in / 0 out.**

**A4 — image pass.** `.pi/screens/after-fix.png` (60 KB), ground truth read by eye **before** the call
(4 presence atoms + 2 absent controls):

| id | truth | answer | max prob | confidence |
|---|---|---|---|---|
| i01 "Create template" button | PRESENT | PRESENT | 0.9580 | 0.8913 |
| i02 "Constraints" panel | PRESENT | PRESENT | 0.9061 | **0.7660** |
| i03 "Subject line (tags allowed)" placeholder | PRESENT | PRESENT | 0.9683 | 0.9172 |
| i04 menu item "Add streams" | PRESENT | PRESENT | 0.9568 | 0.8882 |
| i05 **control** "Attendance" panel | ABSENT | ABSENT | 0.9666 | 0.9129 |
| i06 **control** "Guardians" column | ABSENT | ABSENT | 0.9664 | 0.9123 |

**6/6 correct · 2/2 controls · 1,059 ms · 1,875 in / 0 out.**

**Cost.** (16,774 + 1,875 + 147×2) ≈ 18,943 input tokens at $0.24/1M ≈ **$0.0045** for the whole spike.

**A5 — route.** Recorded above. Local Ollama proxies the cloud tag; a pass run from CI would name
`ollama.com` directly instead, and the record must say which.

### Probe sitting 2 — permutation stability (Q28): PASS on both payloads

Same claims, **order reversed then interleaved**; nothing else changed.

| Leg | Payload | Stable | Detail |
|---|---|---|---|
| Text | the 45,590-byte mock source, 13 claims | **13/13** | every answer identical to the first pass · 1,915 ms · 16,774 in / 0 out |
| Image | `after-fix.png` **cropped to its right column** (520×420 — 80% of the pixels gone), 5 claims re-ordered | **5/5** | every answer identical · 784 ms · 943 in / 0 out · 18 KB |

Answers survived both a question-order change and the removal of four-fifths of the image, so §6.4's stability
check is load-bearing and cheap, not decorative. `c08` ("one row per student in the cohort") landed at **0.8943**
here and **0.8952** in the first pass — the lowest row in both runs.

### Probe sitting 3 — the rendered-DOM leg (Q30–Q31): PASS

**How, without booting the app:** bUnit 2.7.2 (already a dependency of five test projects) rendered the real
`PeriodUpsert` component through a **temporary test file** — created, run in **3.9 s**, deleted before anything was
staged. No AppHost, no database, no Keycloak, no lock on build outputs. It produced **45,758 characters** of genuine
component markup: Blazor scoped-CSS attributes (`b-6hc204vvzg`), FluentUI web-component tags, and two rendered
date-picker calendar grids.

| id | truth | answer | max prob |
|---|---|---|---|
| d01 "Edit period" heading | PRESENT | PRESENT | 0.9736 |
| d02 "Period name" label | PRESENT | PRESENT | **0.8836** |
| d03 "Division" label | PRESENT | PRESENT | 0.8983 |
| d04 "Activation tolerance (days)" label | PRESENT | PRESENT | 0.9637 |
| d05 "Auto-split into 3" button | PRESENT | PRESENT | 0.9479 |
| d06 "No sub-periods yet." text | PRESENT | PRESENT | 0.9511 |
| d07 "Cancel" button | PRESENT | PRESENT | 0.9721 |
| d08 "Sub-periods" heading | PRESENT | PRESENT | 0.9471 |
| d09 the immutability hint | PRESENT | PRESENT | 0.9318 |
| d10 scoped-CSS attributes present | PRESENT | PRESENT | 0.9216 |
| x11 **control** "Student name" label | ABSENT | ABSENT | 0.9718 |
| x12 **control** "Sign-off" column | ABSENT | ABSENT | 0.9616 |
| x13 **control** "Publish" button | ABSENT | ABSENT | 0.8917 |

**13/13 correct · 3/3 controls · 13/13 above threshold · 1,475 ms · 21,285 in / 0 out.**

**Three findings worth carrying:**

1. **Token arithmetic is worse than the byte count suggests.** 45.8 KB of rendered markup = **21,285 tokens =
   32.5% of the 65,536 cloud cap**, so at most **~3** such captures fit one request. Much of it is markup noise a
   human would not call content — two date-picker calendar grids, scoped-CSS attributes on every element. A DOM
   capture must be **pruned to the section of interest**, not dumped whole (§6.5).
2. **A third independent pass still lands under 0.90.** `d02` at **0.8836** is the lowest row of any pass; `x13`
   at 0.8917 joins `c08` (0.8943 / 0.8952) and `i02`'s `confidence` below its top probability. The §6.3 refusal of
   a 0.90 threshold now rests on three payloads, not one.
3. **The scoring harness is part of the evidence, and it needs its own check.** My first DOM run reported
   `p=NaN` for all 13 rows because of a missing spread in `Math.max(Object.values(...))` — a bug whose symptom is
   **indistinguishable from an API that omitted `probabilities`**. The fail-closed comparison (`NaN >= 0.80` is
   false) meant the bug produced "no rows above threshold" rather than a false pass, which is the right failure
   direction — but a pass should assert at least one real probability before reporting, exactly as §11 now requires.

## Review (self-run, Tier 0)

- **Standards.** Scripts are throwaway and out of the tree (`.pi/tmp/`); no production file touched; no
  build or test target changed. Claims and ground truth were written from `grep`-verified source, not from
  memory.
- **Spec.** The probe tested exactly A1–A5, nothing adjacent. It did **not** test the ANLI cliff (§6.2) —
  by design, since atomisation forbids those questions.

**Two findings worth carrying into adoption:**

1. **§6.3 is confirmed empirically, not just by the vendor example.** `confidence` ran **below** `max prob`
   on several rows — i02 0.9061 vs **0.7660**, c08 0.8952 vs 0.7404, c01 0.9214 vs 0.8031. A 0.80 threshold
   on `confidence` would have rejected i02 and nearly rejected c01, while the probability threshold passed
   all 19 rows. The "never threshold `confidence`" rule is load-bearing, not pedantry.
2. **Self-consistency is the spike's ceiling.** The claims, the ground truth and the scoring were all
   authored by the same agent; the absent controls defend against a yes-saying model, not against author
   bias. This is why the bar could pass and adoption still waits for a real artifact.

## Acceptance

| # | Criterion | Evidence | Verdict |
|---|---|---|---|
| A1 | Type spelling known | `noul` 200 / `bool` 400 | **MET** |
| A2 | ≥12/13 atom claims at threshold | 13/13 correct, 13/13 ≥0.80 | **MET** |
| A3 | 3/3 absent controls | 3/3 | **MET** |
| A4 | Image pass matches hand-read | 6/6, 2/2 controls | **MET** |
| A5 | Route recorded | local Ollama 0.40.8 → ollama.com | **MET** |
| A6 | Permutation stability (§6.4) | text 13/13, image 5/5 stable under reorder + crop | **MET** |
| A7 | Threshold recalibration decided on evidence (Q27) | 0.80 kept; 0.90 refused — `c08` at 0.8943 / 0.8952 | **MET** |
| A8 | DOM-text pass on real rendered markup (Q26/Q29/Q30) | bUnit-rendered `PeriodUpsert`, 13/13 + 3/3 controls, 13/13 above threshold | **MET** |

**Verdict: CLOSED for the mechanism on all three payloads.** Text (mock source), image (a real page, plus an
80% crop) and rendered DOM (a real component) each scored perfectly with controls, stable under permutation, all
above the 0.80 threshold, for about a cent. **Adoption is a separate round**, and by Q33/Q34 it now waits on a
consumer rather than on evidence: no protected file is touched until a live round needs the seat.

Not accepted here, and not claimed: the seat is **not adopted**. §8's four documents, the protected-file
reviews and the first real use remain open, per spec §9 Q23–Q29. Spec corrections this round earns: §5.3
(`noul` settled), §6.3 (the 0.90 refusal, from evidence), §6.4 (permutation verified) and §6.6 (payload
ranking) — applied to the spec after this doc, in the same change set.

## Handover (Q35–Q37, same day)

Closing the arc exposed one claim that the repo could not keep: "usable as method" described a harness that
existed only as three gitignored files in `.pi/tmp/`. Q35 settled it by checking the method in, and the
script was then **run before being claimed** — a 5-claim DOM probe, 5/5 above threshold and 5/5 correct with
2/2 absent controls, and a `--permute` re-run stable on all five.

| Item | Where |
|---|---|
| Harness, as a script | `scripts/round/decision-probe.mjs` — claims file in, answer table with probabilities out; fail-closed on a missing probability; never thresholds `confidence` |
| Its documentation | `scripts/round/README.md` — its row, its section, and why it is **not** wired into `gates.sh` |
| Reciprocal pointer | `documents/specs/round-runner-model-policy.md` § "Related harness documents" — the policy spec now names this seat and records that it amends no decision |
| Delivery | Q37: push and open a docs PR. The two owner-accepted lessons stand on their own; the spec and this record land with them |

The parked round (`round-assignment-view-page.md`) is untouched and remains blocked on its owner deliverable;
by Q34 it is explicitly **not** the seat's first consumer.
