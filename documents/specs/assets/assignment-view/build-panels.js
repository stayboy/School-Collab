const fs = require('fs');
const P = require('./build-parts.js');
const { style, pill, btn, panel, count, spacer, mat, ct, grp, tiles, appBar, facts, head } = P;

const OPEN = '<div class="region-grid-host"><div class="region-grid">';
const closeGrid = '</div></div>';
const main = (s) => `<div class="region-grid__main">${s}</div>`;
const rail = (s) => `<div class="region-grid__rail">${s}</div>`;

const details = (rows) =>
  panel('Details', '', facts(rows), 'panel--borderless');

// ── Content & Resources: the settled rail panel, with the grounding readout per row ──────────
const contentPanel = (editable) =>
  panel('Content &amp; Resources', count(5) + spacer + btn(editable ? 'Edit materials' : 'Manage', editable ? 'primary' : ''),
    mat('¶', 'Starter prompts', 'Text · 1,240 characters', 'grounds AI', 'success') +
    mat('▸', 'BBC Bitesize — fractions', 'Link · bbc.co.uk/bitesize/…', 'not indexed') +
    mat('▶', 'Worked example — thirds and quarters', 'Video · 4:12 · 38 MB', 'no text') +
    mat('▣', 'Number line poster', 'Image · PNG · 1.4 MB', 'no text') +
    mat('♪', 'Teacher introduction', 'Audio · 1:38', 'no text') +
    `<div style="display:flex; align-items:center; gap:10px"><div class="funnel" style="flex:1">
       <i style="width:40%; background:var(--status-info-dot)"></i></div>
       <span class="muted">2 of 5 grounding slots</span></div>
     <p class="subtle-note" style="margin:0">Grounding is URL-derived text plus extracted attachment text — five slots,
       20,000 characters each. Instructions and teacher notes are <b>not</b> sent to the model. Preview opens a
       read-only dialog by kind; edits deep-link to the edit surface.</p>`);

const questionsPanel = (editable) =>
  panel('Questions', count(8) + spacer + btn(editable ? 'Edit questions' : 'Manage questions', editable ? 'primary' : ''),
    `<div style="display:flex; gap:6px; flex-wrap:wrap"><span class="chip chip--muted">5 multiple choice</span>
       <span class="chip chip--muted">2 short answer</span><span class="chip chip--muted">1 true / false</span></div>
     <table class="grid"><thead><tr><th style="width:34px">#</th><th>Question</th><th style="width:120px">Type</th>
       <th style="width:70px">Options</th></tr></thead><tbody>
       <tr><td>1</td><td>Which fraction is equivalent to 0.75?</td><td>Multiple choice</td><td>4</td></tr>
       <tr><td>2</td><td>Convert 3⁄8 to a percentage.</td><td>Short answer</td><td>—</td></tr>
       <tr><td>3</td><td>0.6 is greater than 2⁄3.</td><td>True / false</td><td>2</td></tr>
       <tr><td colspan="4" class="muted">5 more — the whole set renders here; no paging under 12 rows</td></tr>
     </tbody></table>`);

// ── Delivery: the new post-publish panel (the answer to "published to whom, and did they get it?") ──
// Delivery (post-publish) — a TABLE in main, same shape as the draft's Targets & Audience (owner, 2026-10-29).
// PRIVACY: no contact VALUE is rendered anywhere on this page. Channel kind and count only — an address or a
// phone number never reaches the payload, the DOM or a screenshot (Q154).
const deliveryPanel = () =>
  panel('Delivery', count(28) + spacer + pill('info', 'published 4 Nov, 09:14'),
    `<p class="subtle-note" style="margin:0">28 students · 31 contacts · 19 primary · 12 other ·
       <b>23 reached</b></p>
     <table class="grid"><thead><tr>
       <th style="width:250px">Student</th><th style="width:250px">Guardian(s)</th>
       <th style="width:150px">Contacts</th><th>Delivery</th></tr></thead><tbody>
       <tr><td><b>Amara Okafor</b> <span class="muted">G7-0142</span></td>
         <td>N. Okafor ${pill('success', 'primary')}<br>J. Okafor ${pill('neutral', 'CC')}</td>
         <td>3 <span class="muted">· email, SMS</span></td>
         <td>${pill('success', 'opened')} <span class="muted">6 Nov, 14:02</span></td></tr>
       <tr><td><b>Daniel Whitfield</b> <span class="muted">G7-0157</span></td>
         <td>D. Whitfield ${pill('success', 'primary')}</td>
         <td>2 <span class="muted">· email, SMS</span></td>
         <td>${pill('danger', 'failed')} <span class="muted">hard bounce · retry 16:20</span></td></tr>
       <tr><td><b>Priya Raman</b> <span class="muted">G7-0163</span></td>
         <td><span class="muted">no guardian on file</span></td>
         <td><span class="muted">none subscribed</span></td>
         <td>${pill('neutral', 'not reached')}</td></tr>
       <tr><td><b>Sofia Marchetti</b> <span class="muted">G7-0191</span></td>
         <td>L. Marchetti ${pill('success', 'primary')}</td>
         <td>1 <span class="muted">· WhatsApp</span></td>
         <td>${pill('warning', 'skipped')} <span class="muted">channel blocked by policy</span></td></tr>
       <tr><td><b>Marcus Bell</b> <span class="muted">G7-0188</span></td>
         <td>M. Bell ${pill('success', 'primary')}</td>
         <td>1 <span class="muted">· email</span></td>
         <td>${pill('danger', 'no more retries')}</td></tr>
       <tr><td colspan="4" class="muted">5 of 28 shown · capped at 200; truncation is always stated</td></tr>
     </tbody></table>
     <p class="subtle-note" style="margin:0">Delivery state comes from the notification log; "opened" from the
       recipient's deep-link stamp. Addresses are not shown — see the privacy note in the spec.</p>
     <div style="display:flex; gap:6px">${btn('View all 28 students')}</div>`);

const progressPanel = () =>
  panel('Progress', spacer + '<span class="chevron">▾</span>',
    tiles([[28, 'cohort'], [27, 'reached'], [21, 'started'], [18, 'submitted'], [12, 'graded'], [9, 'signed']]) +
    `<div class="funnel"><i style="width:43%; background:var(--status-success-dot)"></i>
       <i style="width:21%; background:var(--status-info-dot)"></i>
       <i style="width:36%; background:var(--neutral-stroke-rest)"></i></div>
     <div class="f-legend"><span><em style="background:var(--status-success-dot)"></em>graded 12</span>
       <span><em style="background:var(--status-info-dot)"></em>submitted, ungraded 6</span>
       <span><em style="background:var(--neutral-stroke-rest)"></em>not submitted 10</span></div>` +
    btn('Open progress'));

const audienceExpected = () =>
  panel('Targets &amp; Audience', spacer + '<span class="chip chip--muted">not yet published</span>',
    `<div style="display:flex; gap:6px; flex-wrap:wrap"><span class="chip">Grade 7</span>
       <span class="chip">Stream A</span><span class="chip">+ 2 named students</span></div>` +
    tiles([[28, 'students matched'], [19, 'primary guardians'], [12, 'other contacts']]) +
    `<p class="subtle-note" style="margin:0"><b>Expected</b>, not yet a record — resolved live from policy and
       re-resolved at publish. Names appear once the assignment is published.</p>
     <div style="display:flex; gap:6px">${btn('Preview cohort')}${btn('Publish…', 'primary')}</div>`);

// Pre-publish audience TABLE (owner, 2026-10-29). Targets & Audience moves into MAIN on a draft because a
// student x guardian x contact grid cannot render in the 320-460px rail track. The list is a RESOLVED PREVIEW,
// recomputed at publish — nothing here is a record, and the panel says so.
const audiencePreview = () =>
  panel('Targets &amp; Audience', spacer + '<span class="chip chip--muted">not yet published</span>',
    `<div style="display:flex; gap:6px; flex-wrap:wrap; align-items:center">
       <span class="chip">Grade 7</span><span class="chip">Stream A</span><span class="chip">+ 2 named students</span>
       <span class="muted">28 students matched · 19 primary guardians · 12 other contacts</span></div>
     <table class="grid"><thead><tr>
       <th style="width:250px">Student</th><th style="width:270px">Guardian(s)</th>
       <th style="width:150px">Contacts</th><th style="width:140px">At publish</th></tr></thead><tbody>
       <tr><td><b>Amara Okafor</b> <span class="muted">G7-0142</span></td>
         <td>N. Okafor ${pill('success', 'primary')}<br>J. Okafor ${pill('neutral', 'CC')}</td>
         <td>2 <span class="muted">· email, SMS</span></td>
         <td>${pill('success', 'will notify')}</td></tr>
       <tr><td><b>Daniel Whitfield</b> <span class="muted">G7-0157</span></td>
         <td>D. Whitfield ${pill('success', 'primary')}</td>
         <td>1 <span class="muted">· email</span></td>
         <td>${pill('success', 'will notify')}</td></tr>
       <tr><td><b>Priya Raman</b> <span class="muted">G7-0163</span></td>
         <td><span class="muted">no guardian on file</span></td>
         <td><span class="muted">none subscribed</span></td>
         <td>${pill('neutral', 'no contact')}</td></tr>
       <tr><td><b>Sofia Marchetti</b> <span class="muted">G7-0191</span></td>
         <td>L. Marchetti ${pill('success', 'primary')}</td>
         <td>1 <span class="muted">· WhatsApp</span></td>
         <td>${pill('warning', 'blocked by policy')}</td></tr>
       <tr><td><b>Marcus Bell</b> <span class="muted">G7-0188</span></td>
         <td>M. Bell ${pill('success', 'primary')}</td>
         <td>1 <span class="muted">· email</span></td>
         <td>${pill('success', 'will notify')}</td></tr>
       <tr><td colspan="4" class="muted">5 of 28 shown · capped at 200; truncation is always stated</td></tr>
     </tbody></table>
     <p class="subtle-note" style="margin:0"><b>Recomputed at publish.</b> This list is a resolved preview of who
       would be notified — it is not a record and it changes with the targets, the guardians' contacts and the
       notification policy. <b>Channel kind and count only: the page never renders an address or a phone number</b>
       (Q154).</p>
     <div style="display:flex; gap:6px">${btn('Refresh list')}${btn('Publish…', 'primary')}</div>`);

// Status strip (owner, 2026-10-29): Progress, Guardian sign-off and Notifications were three separate rail
// panels and each one dragged the page down. They are read-outs of the same cohort, so they become THREE
// SEGMENTS OF ONE PanelSection in main, divided by coloured vertical rules, sitting below Delivery.
const kv = (label, value) => `<div class="glance__kv"><span>${label}</span><b>${value}</b></div>`;

const glancePanel = () =>
  panel('Status', spacer + btn('Open progress'),
    `<div class="glance">
       <div class="glance__seg glance__seg--info">
         <h4>Progress</h4>
         ${kv('Cohort', 28)}${kv('Reached', 27)}${kv('Started', 21)}${kv('Submitted', 18)}
         ${kv('Graded', 12)}${kv('Signed', 9)}
         <div class="funnel"><i style="width:43%; background:var(--status-success-dot)"></i>
           <i style="width:21%; background:var(--status-info-dot)"></i>
           <i style="width:36%; background:var(--neutral-stroke-rest)"></i></div>
         <div class="f-legend"><span><em style="background:var(--status-success-dot)"></em>graded 12</span>
           <span><em style="background:var(--status-info-dot)"></em>ungraded 6</span>
           <span><em style="background:var(--neutral-stroke-rest)"></em>not submitted 10</span></div>
       </div>
       <div class="glance__seg glance__seg--success">
         <h4>Guardian sign-off</h4>
         ${kv('Signed', 9)}${kv('Awaiting signature', 3)}${kv('Reassigned', 2)}
         <p class="subtle-note" style="margin:6px 0 0">Applies to the 14 students whose guardian review is
           required.</p>
       </div>
       <div class="glance__seg glance__seg--warning">
         <h4>Notifications</h4>
         ${kv('Failed', 3)}${kv('Retry scheduled', 1)}${kv('Skipped by policy', 2)}
         <p class="subtle-note" style="margin:6px 0 0">Read-only. Each failure also appears on its row in
           Delivery.</p>
       </div>
     </div>`);

module.exports = { OPEN, closeGrid, main, rail, details, contentPanel, questionsPanel, deliveryPanel,
  progressPanel, audienceExpected, audiencePreview, glancePanel, pill, btn, panel, count, spacer, ct, grp, tiles, appBar, facts, head, style };
