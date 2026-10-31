#!/usr/bin/env node
// decision-probe — the decision-model seat's harness, as a script.
//
// Spec:  documents/specs/decision-model-seat.md  (§5 mechanism, §6 question contract)
// Record: documents/rounds/round-decision-model-spike.md (proven on text, image and rendered DOM)
//
// It answers a CLOSED list of atom-level claims about one artifact and reports each answer with its
// probability. It is advisory evidence: never a gate, never an acceptance, never an owner decision.
//
// Usage
//   node scripts/round/decision-probe.mjs --claims claims.json --state page.html [options]
//
// Options
//   --claims <path>     claims file (required) — see shape below
//   --state <path>      text artifact to judge (file path); omit if the claims file has no state
//   --images <paths>    comma-separated PNG/JPEG/WebP files, at most 4 (atomic visual claims only)
//   --threshold <n>     choice threshold on MAX probability (default 0.80; noul uses 0.90)
//   --permute           send the questions in a reversed/interleaved order (stability re-run)
//   --base <url>        API base (default http://localhost:11434)
//   --model <id>        model tag (default clef:27b-cloud; local `clef:27b` also works)
//   --json              emit the raw result JSON instead of a table
//
// Claims file shape
//   {
//     "claims": [
//       { "id": "c01", "type": "choice", "question": "...", "expect": "PRESENT" },
//       { "id": "c02", "type": "noul",   "question": "...", "expect": true }
//     ]
//   }
//   "type" defaults to "choice" over the repo's four verdict words; "expect" is optional ground
//   truth for scoring. A claim list must be DERIVED FROM AN EXTERNAL ARTIFACT (mock, spec, ticket)
//   — a claim the author invented grades the author, not the repository (spec §6.4 item 0).
//
// Exit codes: 0 all rows answered and (if expect was given) all correct · 1 something failed ·
//             2 usage or transport error.
//
// Notes that are load-bearing, not decoration:
//   * A missing or non-numeric probability means NOT ANSWERED — never a pass (spec §6.3, fail closed).
//   * `confidence` is never thresholded: it is a different quantity from the top probability.
//   * The response is checked for at least one real probability before anything is reported (§11):
//     a broken scorer and an API that omitted the field look identical from the output.

import { readFileSync } from 'node:fs';

const VERDICTS = ['PRESENT', 'PARTIAL', 'ABSENT', 'UNVERIFIABLE'];
const VERDICT_CRITERIA = {
  PRESENT: 'The artifact contains it exactly as described.',
  PARTIAL: 'Something similar is present but not as described.',
  ABSENT: 'The artifact does not contain it.',
  UNVERIFIABLE: 'The artifact cannot settle this.',
};

function die(msg, code = 2) {
  console.error(`decision-probe: ${msg}`);
  process.exit(code);
}

function parseArgs(argv) {
  const out = { threshold: 0.80, base: 'http://localhost:11434', model: 'clef:27b-cloud' };
  for (let i = 2; i < argv.length; i++) {
    const a = argv[i];
    const next = () => argv[++i] ?? die(`${a} needs a value`);
    if (a === '--claims') out.claims = next();
    else if (a === '--state') out.state = next();
    else if (a === '--images') out.images = next().split(',').map((s) => s.trim()).filter(Boolean);
    else if (a === '--threshold') out.threshold = Number(next());
    else if (a === '--base') out.base = next();
    else if (a === '--model') out.model = next();
    else if (a === '--permute') out.permute = true;
    else if (a === '--json') out.json = true;
    else if (a === '--help' || a === '-h') { console.log(readFileSync(new URL(import.meta.url)).toString().split('\n').slice(1, 43).map((l) => l.replace(/^\/\/ ?/, '')).join('\n')); process.exit(0); }
    else die(`unknown argument ${a}`);
  }
  if (!out.claims) die('--claims is required');
  if (out.images && out.images.length > 4) die('at most 4 images per request (cloud limit)');
  return out;
}

const args = parseArgs(process.argv);
const spec = JSON.parse(readFileSync(args.claims, 'utf8'));
const claims = spec.claims ?? die('claims file has no "claims" array');
if (claims.length < 1 || claims.length > 64) die('the API takes 1–64 questions');

// Order: as written, or a stability permutation (reversed, then interleaved).
const ordered = args.permute
  ? [...claims.filter((_, i) => i % 2 === 0).reverse(), ...claims.filter((_, i) => i % 2 === 1)]
  : claims;

const questions = {};
for (const c of ordered) {
  const type = c.type ?? 'choice';
  questions[c.id] = type === 'choice'
    ? { type, instructions: c.question, criteria: VERDICT_CRITERIA }
    : { type, instructions: c.question };
}

const body = { model: args.model, questions };
if (args.state) body.state = readFileSync(args.state, 'utf8');
if (args.images?.length) body.images = args.images.map((p) => readFileSync(p).toString('base64'));
if (!body.state && !body.images) die('give --state, --images, or both');

const res = await fetch(`${args.base}/v1/systemone`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
});
const payload = await res.json().catch(() => ({ error: 'response was not JSON' }));
if (!res.ok || payload.error) die(`API refused: ${res.status} ${payload.error ?? ''}`, 1);

if (args.json) { console.log(JSON.stringify(payload, null, 2)); process.exit(0); }

const answers = payload.answers ?? {};
let answered = 0, correct = 0, scored = 0, controls = 0, controlsOk = 0, sawReal = false;

console.log(`model=${payload.model ?? args.model}  usage=${JSON.stringify(payload.usage)}  order=${args.permute ? 'permuted' : 'as written'}\n`);
console.log('id                       ask        answer       maxProb   bar    status');

for (const c of ordered) {
  const a = answers[c.id];
  const probs = a?.probabilities;
  // fail closed: no real probability ⇒ not answered
  const maxProb = probs && Object.values(probs).every((v) => typeof v === 'number')
    ? Math.max(...Object.values(probs))
    : (typeof a?.noul === 'number' ? a.noul : NaN);
  const bar = (c.type ?? 'choice') === 'noul' ? 0.90 : args.threshold;
  if (Number.isFinite(maxProb)) sawReal = true;
  const answer = (c.type ?? 'choice') === 'noul'
    ? (Number.isFinite(maxProb) ? maxProb >= 0.5 : undefined)
    : a?.choice;

  const ok = Number.isFinite(maxProb) && maxProb >= bar;
  if (ok) answered++;
  if (c.expect !== undefined) {
    scored++;
    if (answer === c.expect) correct++;
    if (c.expect === 'ABSENT' || c.expect === false) { controls++; if (answer === c.expect) controlsOk++; }
  }
  const ask = c.type === 'noul' ? 'yes/no' : String(c.expect ?? '—');
  const shown = c.type === 'noul' ? String(answer) : String(answer ?? '(none)');
  const status = !Number.isFinite(maxProb) ? 'NOT ANSWERED' : ok ? 'answered' : 'below bar';
  console.log(`${c.id.padEnd(24)} ${ask.padEnd(10)} ${shown.padEnd(12)} ${Number.isFinite(maxProb) ? maxProb.toFixed(4) : '—'.padEnd(6)}   ${bar.toFixed(2)}   ${status}`);
}

if (!sawReal) die('no question returned a real probability — harness or transport defect, nothing is reportable (§11)', 1);

console.log(`\nanswered ${answered}/${ordered.length} above bar${scored ? `  ·  scored ${correct}/${scored} correct` : ''}${controls ? `  ·  controls ${controlsOk}/${controls}` : ''}`);
const pass = answered === ordered.length && (scored === 0 || correct === scored);
process.exit(pass ? 0 : 1);
