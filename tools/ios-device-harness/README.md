# iOS device harness

Self-verifying fixture page that reproduced the WebKit blank-image capture bug on a real iPhone
(iPhone 11, iOS 18.7, Safari 26.2) and validated the fix that ships in this package - see the
"iOS/WebKit blank images and hangs" section of the main README.

`index.html` renders 13 fixtures (small file-based images, a data-URL image, a lazy image, a CSS
`background-image`, an inline SVG, two SVG logos, four client-generated 1200x1200 noise images for
decode pressure) and captures them at `pixelRatio: 4` through five pipelines: html-to-image 1.11.11
stock, 1.11.13 stock, a stabilise-until-identical loop, a pre-decode variant (proven NOT to work),
and this package's interop module. Verdicts are pixel-sampled per fixture, cold and warm, rendered
into the page and POSTed as JSONL to `/result`.

Run: serve this directory with `node server.js` (port 9099; `serve.py` is the python equivalent)
and open `index.html` from the phone's Safari. Results land in `results.jsonl` next to the server.
Heavy assets are generated client-side so a mid-run network drop cannot invalidate captures.

`results-iphone11-ios18.7-reference.jsonl` is the reference run: stock pipelines blank all four
large images (cold AND warm), the stabilised loop converges to a complete capture by attempt 2-3,
and the interop pipeline passes 27/27.
