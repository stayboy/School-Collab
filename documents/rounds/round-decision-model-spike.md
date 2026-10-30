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
| A8 | DOM-text pass on a real page (Q26/Q29) | not run — no instance; booting one would lock build outputs | **NOT MET** (recorded, not skipped) |

**Verdict: CLOSED for the mechanism, PARKED for adoption.** The seat is real, cheap, stable and
threshold-sane on both payloads exercised; its **default implementation payload (rendered DOM) is unproven on a
real page**, so adopting it into two protected files now would spend that review on a half-proven design.
Adoption resumes with the DOM leg at the parked round's live-render probe.

Not accepted here, and not claimed: the seat is **not adopted**. §8's four documents, the protected-file
reviews and the first real use remain open, per spec §9 Q23–Q29. Spec corrections this round earns: §5.3
(`noul` settled), §6.3 (the 0.90 refusal, from evidence), §6.4 (permutation verified) and §6.6 (payload
ranking) — applied to the spec after this doc, in the same change set.
