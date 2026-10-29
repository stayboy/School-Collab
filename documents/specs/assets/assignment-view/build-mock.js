const fs = require('fs');
const B = require('./build-panels.js');
const { OPEN, closeGrid, main, rail, details, contentPanel, questionsPanel, deliveryPanel,
  progressPanel, audienceExpected, audiencePreview, pill, btn, panel, count, spacer, ct, grp, tiles, appBar, facts, head,
  style } = B;

const signOffPanel = () => panel('Guardian sign-off', spacer + '<span class="chevron">▾</span>',
  `<div class="ct"><span class="ct__val">Signed</span>${pill('success', '9')}</div>
   <div class="ct"><span class="ct__val">Awaiting signature</span>${pill('warning', '3')}</div>
   <div class="ct"><span class="ct__val">Reassigned guardian</span>${pill('neutral', '2')}</div>
   <p class="subtle-note" style="margin:0">Applies to the 14 students whose guardian review is required.</p>`);

const notificationsPanel = () => panel('Notifications', spacer + '<span class="chevron">▾</span>',
  `<div class="ct"><span class="ct__val">Failed</span>${pill('danger', '3')}</div>
   <div class="ct"><span class="ct__val">Retry scheduled</span>${pill('warning', '1')}</div>
   <div class="ct"><span class="ct__val">Skipped by policy</span>${pill('neutral', '2')}</div>
   <p class="subtle-note" style="margin:0">Read-only. Each failure also appears on its contact row in Delivery.</p>`);

const rulesPanel = (approval) => panel('Rules &amp; policy', spacer + '<span class="chevron">▾</span>',
  `<table class="grid"><tbody>
     <tr><td>Approval before publish</td><td class="muted">${approval}</td></tr>
     <tr><td>Notification policy</td><td class="muted">Resolved per recipient grade</td></tr>
     <tr><td>Archive window</td><td class="muted">30 days after the due date</td></tr>
   </tbody></table>`);

const approvalPanel = () => panel('Approval', spacer + pill('warning', 'pending'),
  `<p class="subtle-note" style="margin:0">Submitted by J. Mensah on 28 Oct 2026, 17:45. The window closes
     automatically and returns the assignment to the author.</p>
   <div style="display:flex; gap:6px; flex-wrap:wrap">${btn('Approve', 'primary')}${btn('Reject')}
     <button class="btn btn--sm" title="Terminal — the author must duplicate the assignment to continue">Deny</button></div>
   <p class="muted" style="margin:0">Deny is terminal; Reject lets the author resubmit.</p>`);

// ── A · published ────────────────────────────────────────────────────────────────────────────
const secA = `
<section>
  <div class="sec-h"><span class="tag">A</span><h2>Published — the view page</h2>
    <span class="note">one header · main + rail · primary follows the §11 matrix</span></div>
  <div class="app">${appBar}<div class="content">
    <div class="crumbs">Assignments › Grade 7 Mathematics › <b>Fractions — Unit 3 checkpoint</b></div>
    ${head(pill('info', 'Published', false) + '<span class="chip">Guardian review required</span>',
      btn('Open progress', 'primary'))}
    ${OPEN}${main(
      details([['Type', 'Digital'], ['Grading', 'Auto-graded'], ['Maximum score', '20 <span class="muted">· pass at 12</span>'],
        ['Due', '12 Nov 2026, 16:00'], ['Attempts', '2'], ['Published', '4 Nov 2026, 09:14']]) +
      questionsPanel(false) +
      // Delivery moves into MAIN as a table (owner, 2026-10-29) — the audience fact belongs beside the work,
      // and it is the tallest panel, so the rail stops outrunning the main column. Progress and Guardian
      // sign-off are aggregates and go back to the rail.
      deliveryPanel())}${rail(
      contentPanel(false) + progressPanel() + signOffPanel() + notificationsPanel() +
      rulesPanel('Not required'))}${closeGrid}
  </div></div>
</section>`;

// ── B · draft, approval outstanding ──────────────────────────────────────────────────────────
const secB = `
<section>
  <div class="sec-h"><span class="tag">B</span><h2>Draft — approval outstanding</h2>
    <span class="note">same skeleton · publish is gated by the matrix, not by the page</span></div>
  <div class="app">${appBar}<div class="content">
    <div class="crumbs">Assignments › Grade 7 Mathematics › <b>Fractions — Unit 3 checkpoint</b></div>
    ${head(pill('neutral', 'Draft', false) + pill('warning', 'Approval pending') + '<span class="chip">expires in 22h 14m</span>',
      btn('Publish', 'primary', ' disabled title="Approval is required before this assignment can be published"'))}
    ${OPEN}${main(
      details([['Type', 'Digital'], ['Grading', 'Auto-graded'], ['Maximum score', '20'], ['Due', '12 Nov 2026, 16:00'],
        ['Attempts', '2'], ['Last saved', '28 Oct 2026, 17:41']]) +
      questionsPanel(true) +
      // Targets & Audience moves to MAIN on a draft and becomes a TABLE (owner, 2026-10-29): the student x
      // guardian x contact grid needs the main column's width, and Approval goes back to the rail where the
      // targets panel was. The list is a resolved preview, recomputed at publish.
      audiencePreview())}${rail(
      approvalPanel() + contentPanel(true) + rulesPanel('Required — grade policy'))}${closeGrid}
  </div></div>
</section>`;


// ── C · delivery, zoomed ─────────────────────────────────────────────────────────────────────
const secC = `
<section>
  <div class="sec-h"><span class="tag">C</span><h2>Delivery — zoomed</h2>
    <span class="note">the one panel that answers “published to whom, and did they get it?”</span></div>
  <div class="app">${appBar}<div class="content"><div style="max-width:820px">
    ${deliveryPanel()}
    <div class="legend" style="margin:16px 0 0; max-width:none">
      <h3>Delivery vocabulary — one rule set, five variants</h3>
      <ul>
        <li>${pill('success', 'sent')} handed to the channel · ${pill('success', 'opened')} the recipient followed the deep link</li>
        <li>${pill('neutral', 'queued')} due now or waiting on a retry · ${pill('warning', 'retry 16:20')} failed, retry scheduled</li>
        <li>${pill('danger', 'no more retries')} terminal failure · ${pill('warning', 'skipped')} suppressed by policy, not an error</li>
        <li>${pill('neutral', 'not reached')} a student with no subscribed contact — the row the current tabs cannot show</li>
      </ul>
    </div>
  </div></div></div>
</section>`;

// ── D · progress page ───────────────────────────────────────────────────────────────────────
const row = (name, code, contacts, state, score, sign, last, action) =>
  `<tr><td><b>${name}</b> <span class="muted">${code}</span></td><td>${contacts}</td>
   <td>${pill(state[0], state[1])}</td><td>${score}</td><td>${sign}</td>
   <td class="muted">${last}</td><td><button class="btn btn--sm btn--ghost">${action}</button></td></tr>`;

const secD = `
<section>
  <div class="sec-h"><span class="tag">D</span><h2>Progress page — <code>/assignments/{id}/progress</code></h2>
    <span class="note">new page · existing reads · one row per student in the cohort</span></div>
  <div class="app">${appBar}<div class="content">
    <div class="crumbs">Assignments › Fractions — Unit 3 checkpoint › <b>Progress</b></div>
    <div class="head"><div class="head__id">
      <div class="head__title"><h1>Progress</h1>${pill('info', 'Published', false)}
        <span class="chip">due 12 Nov 2026</span></div>
      <p class="meta">28 students · 27 reached · 18 submitted · 12 graded</p>
    </div><div class="head__act">${btn('Back to assignment')}</div></div>
    <div style="margin:14px 0">${tiles([[28, 'cohort'], [27, 'reached'], [21, 'started'], [18, 'submitted'], [12, 'graded'], [9, 'signed']], 6)}</div>
    <div class="panel"><div class="panel__head"><h3 class="panel__title">Students</h3>${count(28)}${spacer}
      <div class="filters"><span class="filter filter--on">All 28</span><span class="filter">Not started 10</span>
        <span class="filter">Submitted, ungraded 6</span><span class="filter">Graded 12</span>
        <span class="filter">Not reached 1</span></div></div>
      <table class="grid"><thead><tr><th>Student</th><th style="width:150px">Contacts</th><th style="width:130px">Submission</th>
        <th style="width:70px">Score</th><th style="width:120px">Sign-off</th><th style="width:120px">Last activity</th>
        <th style="width:80px"></th></tr></thead><tbody>
        ${row('Amara Okafor', 'G7-0142', '3 <span class="muted">· 1 opened</span>', ['success', 'graded'], '<b>17</b> <span class="muted">/ 20</span>', pill('success', 'signed'), '6 Nov, 14:02', 'Open')}
        ${row('Daniel Whitfield', 'G7-0157', '2 <span class="muted">· 1 failed</span>', ['info', 'submitted'], '<span class="muted">—</span>', pill('warning', 'awaiting'), '7 Nov, 09:31', 'Mark')}
        ${row('Priya Raman', 'G7-0163', pill('neutral', 'none'), ['success', 'graded'], '<b>15</b> <span class="muted">/ 20</span>', '<span class="muted">not required</span>', '5 Nov, 18:40', 'Open')}
        ${row('Sofia Marchetti', 'G7-0191', '1 <span class="muted">· skipped</span>', ['neutral', 'not started'], '<span class="muted">—</span>', '<span class="muted">—</span>', '—', 'Submit…')}
        ${row('Marcus Bell', 'G7-0188', '1 <span class="muted">· terminal</span>', ['neutral', 'not started'], '<span class="muted">—</span>', '<span class="muted">—</span>', '—', 'Submit…')}
        <tr><td colspan="7" class="muted">22 more · paging arrives only if the cohort cap is reached</td></tr>
      </tbody></table>
      <p class="subtle-note" style="margin:0">Score is blank until graded — an ungraded row is a work item, not a
        missing value. “Submit…” opens the on-behalf path for paper or offline work.</p>
    </div>
  </div></div>
</section>`;

const docHead = `
<h1 class="doc">Assignment view — modernization (v1 mock, post-review)</h1>
<p class="docsub">Four states. Every state renders <b>one</b> page header (no tab strip), a main region and a rail
region per <code>panel-layout-pattern.md</code>: regions draw nothing, each block is one <code>PanelSection</code>,
collapse is a container query with the rail last, colours are tokens only. Structure reflects the
<code>deepseek-v4-pro:0813</code> review: materials sit in the rail as <b>Content &amp; Resources</b>, the panel name
<b>Targets &amp; Audience</b> is kept, and the primary action follows the settled §11 matrix.</p>
<div class="legend"><h3>What to check</h3><ul>
  <li><b>One title, one action pair.</b> Published → primary <i>Open progress</i>, with Unpublish and Close in the
      kebab (§11 view-surface exception, Q141). Draft → primary <i>Publish</i>, gated by approval.</li>
  <li><b>Delivery</b> is the new panel: cohort by student, delivery pill per contact, a dashed <i>not reached</i> row.</li>
  <li><b>Content &amp; Resources</b> lives in the rail and carries the grounding readout — no separate AI panel.</li>
</ul></div>`;

const html = `<!DOCTYPE html>
<html lang="en">
<head><meta charset="utf-8" /><title>Assignment view — modernization v1</title>${style}</head>
<body>${docHead}${secA}${secB}${secC}${secD}
</body>
</html>
`;

fs.writeFileSync(require('path').join(__dirname, 'assignment-view-v1.html'), html, 'utf8');
console.log('written ' + html.length + ' chars');
