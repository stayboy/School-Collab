# Assignment view — mock (spec artifact)

The design record for `documents/specs/assignment-detail-view-modernization.md` (issue #453). The orchestrator's
step 2c requires a mock that **renders** `documents/solution/panel-layout-pattern.md`, so the mock lives in the repo
rather than in a gitignored scratch folder.

## Regenerate

```bash
node build-mock.js      # composes the body -> assignment-view-v1.html
node check-depth.js     # asserts every <section> closes and the rail is a sibling of main
```

`build-mock.js` composes the body from `build-parts.js` (primitives + CSS lifted from the generated file) and
`build-panels.js` (the panels). The body is generated, never hand-typed: the first hand-written version had the rail
nested inside the main column in three of four sections, which is invisible in markup review and obvious in a render.

`check-depth.js` is the guard for exactly that defect. Run it after any markup change; it must print `0` for every
section and `region-grid__main` / `region-grid__rail` at the **same** depth.

## Screenshots

Local only, gitignored (`.pi/mock/state-*.png`), produced by `.pi/mock/shot-assignment-view.js` with Playwright.
The PNGs go stale; the HTML does not. Review the render, not the image in a PR.

## States

| Section | State |
|---|---|
| A | Published — the view page: one header, main + rail, no tabs |
| B | Draft with approval outstanding — publish gated by the matrix |
| C | Delivery zoomed — cohort by student, per-contact delivery state, *not reached* rows |
| D | Progress page — new route, cohort table |
| E/F | Dark and narrow renders of A (the scope-root colour rule and the container-query collapse) |
