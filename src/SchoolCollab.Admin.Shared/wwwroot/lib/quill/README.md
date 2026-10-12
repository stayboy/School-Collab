# Vendored: Quill 2.0.3 (the editor engine behind Blazored.TextEditor)

Round `wysiwyg-instruction-editor` (§8.4 Q12). These are **not** development dependencies — they are the
editor's runtime assets, vendored and pinned locally because **this repository ships no CDN assets**
(§8.5). The wrapper's own setup pulls Quill from `cdn.jsdelivr.net`; each host's `App.razor` therefore
points at *these* files instead, and the CDN lines are deliberately not copied.

| File | Version | Licence | Bytes | SHA-256 (first 16) |
|---|---|---|---|---|
| `quill.js` | 2.0.3 | BSD-3-Clause | 209274 | `F6157C72AC9B3F51…` |
| `quill.snow.css` | 2.0.3 | BSD-3-Clause | 24606 | `1C7948CD13AA92FA…` |

**Quill licence.** BSD-3-Clause (`slab/quill` LICENSE — © 2017-2024 Slab, © 2014 Jason Chen,
© 2013 salesforce.com): permissive, attribution + no-endorsement, no copyleft and no
commercial-use restriction. Full hashes and the licence text live upstream at
`https://github.com/slab/quill` — the summary here exists so the attribution travels with the files.

**The wrapper** (`Blazored.TextEditor` 1.1.3, MIT) is a NuGet package, not a vendored file: its own two
scripts are served by the package at `_content/Blazored.TextEditor/…` and are referenced by the hosts.

**Only `snow` is vendored.** The wrapper's README also mentions `quill.bubble.css`; we use the `snow`
theme (`Theme` default) and omit bubble deliberately, so the vendored surface stays as small as the
feature needs.

**Updating these files** is a version bump plus: re-download, re-measure, update the table above, and
record the new byte sizes in the round doc (AC-7) — never a silent drop-in.
