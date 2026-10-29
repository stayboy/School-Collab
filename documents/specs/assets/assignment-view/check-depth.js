const fs = require('fs');
const path = require('path');
const h = fs.readFileSync(path.join(__dirname, 'assignment-view-v1.html'), 'utf8');

// per-section div balance
const parts = h.split('<section>');
parts.forEach((p, i) => {
  if (i === 0) return;
  let d = 0;
  const re = /<div\b|<\/div>/g;
  let t;
  while ((t = re.exec(p))) d += t[0] === '</div>' ? -1 : 1;
  console.log('section ' + (i - 1) + ' final div depth = ' + d);
});

// where the region divs sit, with depth
let d = 0;
const re = /<div\b|<\/div>/g;
let t;
while ((t = re.exec(h))) {
  d += t[0] === '</div>' ? -1 : 1;
  if (t[0] === '</div>') continue;
  const head = h.slice(t.index, t.index + 140);
  if (/region-grid/.test(head)) {
    const cls = (head.match(/class="([^"]+)/) || [])[1];
    console.log('  ' + cls + ' @ div-depth ' + d);
  }
}
