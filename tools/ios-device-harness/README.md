# iOS device harness

Self-verifying fixture page that measures what html-to-image and this package's interop do on a real
iPhone. The "iOS/WebKit blank images and hangs" section of the main README and
https://github.com/Kebechet/Blazor.HtmlToImage/issues/1 rest on its results.

`index.html` renders 13 fixtures (small file-based images, a data-URL image, a lazy image, a CSS
`background-image`, an inline SVG, two SVG logos, four client-generated 1200x1200 noise images) and
captures them at `pixelRatio: 4` through five pipelines: html-to-image 1.11.11 stock, 1.11.13 stock,
a capture-until-two-identical loop of up to 4 renders, a pre-decode variant, and this package's interop
module with its defaults. Verdicts are pixel-sampled per fixture for a first capture (`cold`) and an
immediate re-capture (`warm`), rendered into the page and POSTed as JSONL to `/result`. Every
`decode()` call is counted per phase and image kind, with its outcome (`decodeStats`).

Other modes:
- `?mode=stress` raises memory pressure step by step, holding up to 192 distinct 2048x2048 bitmaps
  (`&steps=` overrides the steps). At each step it records `decode()` outcomes for the new bitmaps and
  for the capture's SVG, then runs a stock 1.11.13 capture. Pass a new `&token=` per run: it guards
  against a crash-reload restarting the escalation.
- `?mode=background` captures back to back through the interop (1 render, 30 s timeout) for
  `&seconds=` (default 90) while the driver sends Safari to the background and back. It records
  visibility changes, gaps in a 250 ms heartbeat, and each capture's start, end and outcome.

`&run=<id>` tags every report of a run.

## Run

`node server.js` serves this directory on port 9099, and the package's
`src/Blazor.HtmlToImage/wwwroot/html-to-image-interop.js` at `/html-to-image-interop.js`, so the
harness always measures the interop that ships. Open `http://<pc-ip>:9099/?run=<id>` in the phone's
Safari; results append to `results.jsonl` next to the server. Heavy assets are generated client-side,
so a network drop mid-run cannot invalidate captures.

`dotnet run summarize.cs results.jsonl [run]` prints each run; `dotnet run tally.cs results.jsonl`
aggregates the `device-*` runs into the numbers below.

## Reference results

`results-iphone11-ios26.2-reference.jsonl`: iPhone 11, iOS 26.2, 2026-10-28. Safari's user agent
reports a frozen `iPhone OS 18_7`; the real version is its `Version/26.2` token. Runs `device-p1` to
`p4` used the 1.11.13.2 interop (pipeline `pkg-interop`), `device-q1` to `q4` the 1.11.13.3 interop
(`pkg-1.11.13.3`); `captureStable` is the same in both. `device-p5` stopped partway, when another
WebDriverAgent session took the phone, and has no final results.

A capture is complete when none of the four 1200x1200 images is blank. The 9 small fixtures rendered
in every capture of every pipeline.

| Pipeline (8 runs) | First capture complete | Re-capture complete |
|---|---|---|
| 1.11.13 stock | 0/8 | 0/8 |
| 1.11.11 stock | 0/8 | 7/8 |
| Pre-decode every embedded image, then draw | 0/8 | 0/8 |
| Capture until two identical, up to 4 renders | 7/8 | 4/8 |
| Package interop (`captureStable`, 3 renders) | 3/8 | 5/8 |

Two identical blank results end a capture-until-identical loop, which is why neither loop removes the
blanks reliably.

- `decode()`: 1066 calls over all runs, none rejected (SVG data URL 314, bitmap data URL 338, bitmap
  blob 92, file URL 322).
- `device-stress`: from about 92 held bitmaps WebKit failed to load new images (4 of 32 at 96, all 32
  at 128, 160 and 192). No `decode()` rejected, and every capture settled in 1.0-1.6 s.
- `device-background`: JavaScript was suspended about 1 s after Safari was hidden (a 40.2 s heartbeat
  gap). The capture in flight failed with the 30 s timeout as soon as Safari returned, 41.7 s after it
  started; 82 of 83 captures succeeded.
