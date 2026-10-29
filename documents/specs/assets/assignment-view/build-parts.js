// Rebuilds the mock body from helpers, reusing the CSS block from the generated file.
// The hand-written body had three sections with unbalanced <div>s (the rail nested inside main),
// so the structure is composed here instead of typed. Paths are __dirname-relative so the
// generator renders anywhere — never hardcode a machine path in a committed artifact.
const fs = require('fs');
const path = require('path');
const FILE = path.join(__dirname, 'assignment-view-v1.html');
const src = fs.readFileSync(FILE, 'utf8');
const style = src.slice(src.indexOf('<style>'), src.indexOf('</style>') + 8);

const pill = (kind, text, sm = true) =>
  `<span class="pill pill--${kind}${sm ? ' pill--sm' : ''}"><i></i>${text}</span>`;
const btn = (text, kind = '', extra = '') =>
  `<button class="btn btn--sm${kind ? ' btn--' + kind : ''}"${extra}>${text}</button>`;
const panel = (title, head, body, cls = '') =>
  `<div class="panel${cls ? ' ' + cls : ''}"><div class="panel__head"><h3 class="panel__title">${title}</h3>${head}</div>${body}</div>`;
const count = (n) => `<span class="panel__count">${n}</span>`;
const spacer = '<span class="panel__spacer"></span>';

const mat = (ico, title, sub, badge, bk = 'neutral') =>
  `<div class="mat"><span class="mat__ico">${ico}</span><div class="mat__t"><b>${title}</b><span>${sub}</span></div>
   <div class="mat__act">${pill(bk, badge)}${btn('Preview', 'ghost')}</div></div>`;
const ct = (ch, val, role, kind, state) =>
  `<div class="ct"><span class="ct__ch">${ch}</span><span class="ct__val">${val}</span>
   <span class="ct__role">${role}</span>${pill(kind, state)}</div>`;
const grp = (name, code, kind, state, rows, note = '', unreached = false) =>
  `<div class="grp${unreached ? ' grp--unreached' : ''}"><div class="grp__h"><b>${name}</b><span class="code">${code}</span>
   ${spacer}${pill(kind, state)}</div>${rows}${note ? `<p class="muted" style="margin:2px 0 0">${note}</p>` : ''}</div>`;
const tile = (n, label) => `<div class="tile"><b>${n}</b><span>${label}</span></div>`;
const tiles = (items, cols = 3) =>
  `<div class="tiles"${cols !== 3 ? ` style="grid-template-columns:repeat(${cols},minmax(0,1fr))"` : ''}>${
    items.map(([n, l]) => tile(n, l)).join('')}</div>`;

const appBar = `<div class="app-bar"><span class="brand">SchoolCollab</span><span class="sep">/</span><span class="page">Assignments</span></div>`;
const facts = (rows) =>
  `<dl class="fact-grid">${rows.map(([k, v]) => `<div><dt>${k}</dt><dd>${v}</dd></div>`).join('')}</dl>`;

const ASSIGNMENT = 'Fractions, decimals &amp; percentages — Unit 3 checkpoint';
const meta = `<p class="meta"><code>ASG-2026-0412</code> · Grade 7 Mathematics · Digital · Auto-graded · out of 20 · due 12 Nov 2026</p>`;
const head = (badges, primary, secondary = '') =>
  `<div class="head"><div class="head__id"><div class="head__title"><h1>${ASSIGNMENT}</h1>${badges}</div>${meta}</div>
   <div class="head__act">${secondary}${primary}<button class="kebab" title="More actions">⋯</button></div></div>`;

// Pager footer for a data-driven grid (owner rule, 2026-10-29): default 15 per page, per-grid, and a
// rows-per-page control where the collection genuinely grows (questions, delivery/audience students).
const pager = (from, to, total, sizes = false, capped = false) =>
  `<div class="pager"><span>${from}–${to} of ${total}${capped ? ' (capped at 200)' : ''}</span>
   <span class="pager__spacer"></span>
   ${sizes ? '<span>Rows per page</span><span class="pager__size">15 ▾</span>' : ''}
   <span class="pager__nav pager__nav--off">‹</span><span class="pager__nav">›</span></div>`;

module.exports = { style, pill, btn, panel, count, spacer, mat, ct, grp, tile, tiles, appBar, facts, head, pager };
