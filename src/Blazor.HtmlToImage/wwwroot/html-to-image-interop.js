'use strict';

// Set by the JS initializer (Kebechet.Blazor.HtmlToImage.lib.module.js) before any component can
// render. Awaiting the script's own load event is the real readiness signal - polling for the
// global would still be a race on a slow device, only a noisier one.
const READY_PROMISE_KEY = '__kebechetHtmlToImageReady';
const LOAD_FUNCTION_KEY = '__kebechetHtmlToImageLoad';

async function getLibrary() {
	if (!globalThis.htmlToImage) {
		const ready = globalThis[READY_PROMISE_KEY];
		try {
			await ready;
		} catch {
			// The initializer's load failed. Load again rather than rethrowing the same rejection on
			// every later capture; the identity check keeps concurrent captures to a single reload.
			if (globalThis[READY_PROMISE_KEY] === ready) {
				globalThis[READY_PROMISE_KEY] = globalThis[LOAD_FUNCTION_KEY]();
			}
			await globalThis[READY_PROMISE_KEY];
		}
	}

	const library = globalThis.htmlToImage;
	if (!library) {
		throw new Error(
			'html-to-image did not load. The package injects it automatically via its Blazor JS ' +
			'initializer; if this app disables JS initializers, add a script tag for ' +
			'_content/Kebechet.Blazor.HtmlToImage/html-to-image.js instead.');
	}

	return library;
}

const DEFAULT_STABILIZE_ATTEMPTS = 3;
const DEFAULT_CAPTURE_TIMEOUT_MS = 30000;

/**
 * Wrapper-level settings that must never reach html-to-image as options.
 */
function captureSettings(options) {
	const stabilizeAttempts = options?.stabilizeAttempts;
	const captureTimeoutMs = options?.captureTimeoutMs;
	// The C# options are int?, so non-finite values cannot arrive via the typed API - the
	// isFinite guards only protect direct JS callers of this module (Infinity would make the
	// stabilisation loop unbounded, and setTimeout coerces it to an immediate 0 ms timeout).
	return {
		stabilizeAttempts: Number.isFinite(stabilizeAttempts) && stabilizeAttempts >= 1 ? Math.floor(stabilizeAttempts) : DEFAULT_STABILIZE_ATTEMPTS,
		captureTimeoutMs: Number.isFinite(captureTimeoutMs) && captureTimeoutMs > 0 ? Math.floor(captureTimeoutMs) : DEFAULT_CAPTURE_TIMEOUT_MS,
	};
}

/**
 * Upstream's createImage never settles when HTMLImageElement.decode() rejects (Safari does that
 * under memory pressure), so an unbounded await here would hang the capture forever. The bound
 * turns the hang into an error the caller can see.
 * Upstreamed as https://github.com/bubkoo/html-to-image/pull/589; once a vendored release contains
 * it, this remains as a plain safety bound rather than a bug workaround.
 */
function withCaptureTimeout(promise, timeoutMs, label) {
	return Promise.race([
		promise,
		new Promise((_, reject) => setTimeout(
			() => reject(new Error(`html-to-image ${label} did not complete within ${timeoutMs} ms. ` +
				'Large captures can legitimately take longer - raise CaptureTimeoutMs if so.')),
			timeoutMs)),
	]);
}

/**
 * Captures until two consecutive results are identical (WebKit rasterises before large embedded
 * images have decoded, so the first capture of an image-heavy subtree comes back with them blank).
 * `toComparable` reduces a result to a cheaply comparable string; the final RESULT is returned,
 * not the comparable.
 * Upstreamed as https://github.com/bubkoo/html-to-image/pull/591 (opt-in `stabilizationAttempts`);
 * once a vendored release contains it, this loop can be dropped in favour of forwarding
 * `stabilizeAttempts` to that upstream option.
 */
async function captureStable(runCapture, toComparable, settings, label) {
	let previousComparable = null;
	let result = null;
	for (let attempt = 1; attempt <= settings.stabilizeAttempts; attempt++) {
		result = await withCaptureTimeout(runCapture(), settings.captureTimeoutMs, label);
		if (settings.stabilizeAttempts === 1) {
			return result;
		}
		let comparable;
		try {
			comparable = toComparable(result);
		} catch {
			// A tainted canvas throws on toDataURL()/getImageData(); comparison is impossible,
			// so degrade to single-capture behaviour instead of failing a capture that a plain
			// (unstabilised) call would have returned.
			return result;
		}
		if (previousComparable !== null && comparable === previousComparable) {
			return result;
		}
		previousComparable = comparable;
	}
	return result;
}

function resolveNode(target) {
	if (typeof target !== 'string') {
		return target;
	}

	const node = document.getElementById(target);
	if (!node) {
		throw new Error(`No element found with id "${target}".`);
	}

	return node;
}

/**
 * Compiles ExcludeCssClasses / ExcludeSelector into upstream's `filter` predicate.
 * Returning false for a node also excludes its whole subtree, which is what callers expect from
 * "hide this from the screenshot".
 */
function buildFilter(excludeCssClasses, excludeSelector) {
	const hasClasses = Array.isArray(excludeCssClasses) && excludeCssClasses.length > 0;
	const hasSelector = typeof excludeSelector === 'string' && excludeSelector.length > 0;
	if (!hasClasses && !hasSelector) {
		return null;
	}

	return (node) => {
		// html-to-image walks text nodes too, and those carry neither classList nor .matches.
		if (!node || node.nodeType !== Node.ELEMENT_NODE) {
			return true;
		}

		if (hasClasses && node.classList) {
			for (const cssClass of excludeCssClasses) {
				if (node.classList.contains(cssClass)) {
					return false;
				}
			}
		}

		if (hasSelector && typeof node.matches === 'function' && node.matches(excludeSelector)) {
			return false;
		}

		return true;
	};
}

/**
 * ImageLoadFailed carries at most this much of the failed image's source. Upstream reports an image
 * whose bytes do not decode as a data URL of those bytes, routinely larger than the 32 KB message
 * that carries the event on Blazor Server - and an oversized message closes the circuit. The media
 * type and first bytes at the start are what identify the image.
 */
const MAX_REPORTED_IMAGE_SOURCE_LENGTH = 2048;

function withoutUnsetMembers(source) {
	const result = {};
	for (const [key, value] of Object.entries(source)) {
		if (value !== null && value !== undefined) {
			result[key] = value;
		}
	}
	return result;
}

/**
 * Blazor's interop serializer emits every unset nullable as JSON null. Passing those straight
 * through would override html-to-image's own defaults with null - so unset members are dropped
 * here rather than forwarded.
 * fetchRequestInit needs the same one level down: upstream hands it to fetch() as-is, and fetch
 * rejects null for mode, redirect, headers or referrer, so a partly-set FetchRequestInit would fail
 * every resource request.
 */
function buildOptions(options, errorListener) {
	const result = {};
	if (options) {
		for (const [key, value] of Object.entries(options)) {
			if (value === null || value === undefined) {
				continue;
			}
			if (key === 'excludeCssClasses' || key === 'excludeSelector' || key === 'stabilizeAttempts' || key === 'captureTimeoutMs') {
				continue;
			}
			result[key] = key === 'fetchRequestInit' ? withoutUnsetMembers(value) : value;
		}

		const filter = buildFilter(options.excludeCssClasses, options.excludeSelector);
		if (filter) {
			result.filter = filter;
		}
	}

	// Upstream's onImageErrorHandler is a JS callback, so it cannot be an option value on the .NET
	// side. It is wired here only when something is subscribed - an unsubscribed capture passes no
	// reference and therefore pays no interop cost per failed image.
	if (errorListener) {
		result.onImageErrorHandler = (event) => {
			const imageSource = event?.target?.src ?? event?.currentTarget?.src ?? String(event ?? '');
			errorListener.invokeMethodAsync('OnImageLoadFailed', imageSource.slice(0, MAX_REPORTED_IMAGE_SOURCE_LENGTH))
				// Fire-and-forget: a scope torn down mid-capture disposes the reference, and that
				// rejection must not surface as an unhandled one.
				.catch(() => {});
		};
	}

	return result;
}

/**
 * Text results - data URLs, font CSS - go back as a Blob that .NET reads as a stream, never as a
 * string result. On Blazor Server a string result crosses SignalR as a single message, capped at
 * 32 KB by default, and a larger one closes the whole circuit; a data URL is routinely megabytes.
 * The leading byte-order mark keeps the Blob non-empty: Blazor refuses to stream zero bytes, and an
 * empty string is a valid answer (font CSS for a page with no web fonts). .NET strips it again.
 */
function asTextBlob(text) {
	return new Blob(['﻿', text], { type: 'text/plain;charset=utf-8' });
}

export async function toDataUrl(target, format, options, errorListener) {
	return asTextBlob(await captureDataUrl(target, format, options, errorListener));
}

async function captureDataUrl(target, format, options, errorListener) {
	const library = await getLibrary();
	const node = resolveNode(target);
	const resolvedOptions = buildOptions(options, errorListener);
	const settings = captureSettings(options);

	switch (format) {
		case 'png':
			return await captureStable(() => library.toPng(node, resolvedOptions), dataUrl => dataUrl, settings, 'toPng');
		case 'jpeg':
			return await captureStable(() => library.toJpeg(node, resolvedOptions), dataUrl => dataUrl, settings, 'toJpeg');
		case 'svg':
			// toSvg serialises without rasterising, so there is nothing to stabilise - only bound it.
			return await withCaptureTimeout(library.toSvg(node, resolvedOptions), settings.captureTimeoutMs, 'toSvg');
		default:
			throw new Error(`Unsupported data-url format "${format}".`);
	}
}

/**
 * Width and height as little-endian uint32 in front of the RGBA bytes - a wire contract with
 * PixelData.FromInteropBytes on the .NET side.
 */
const PIXEL_DATA_HEADER_BYTES = 8;

function readPixels(canvas) {
	return canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
}

/**
 * Full FNV-1a over the pixel buffer. Sampling would be faster but a hash over every byte cannot
 * miss a region-sized difference, and 13 MB of pixels hashes in tens of milliseconds.
 */
function pixelDigest(pixels) {
	let hash = 0x811c9dc5;
	for (let i = 0; i < pixels.length; i++) {
		hash = ((hash ^ pixels[i]) * 0x01000193) >>> 0;
	}
	return `${pixels.length}:${hash}`;
}

/**
 * Returns the raw Blob / ArrayBuffer, NOT a DotNet.createJSStreamReference(...) wrapper.
 *
 * Because the .NET side declares the result as IJSStreamReference, Blazor's own result converter
 * calls createJSStreamReference on whatever this returns. Wrapping it here too means the framework
 * re-wraps an already-wrapped marker object, which is neither a Blob nor a typed array, and the
 * call dies with "Supplied value is not a typed array or blob."
 */
export async function toStream(target, format, options, errorListener) {
	const library = await getLibrary();
	const node = resolveNode(target);
	const resolvedOptions = buildOptions(options, errorListener);
	const settings = captureSettings(options);

	if (format === 'pixelData') {
		// Deliberately NOT library.toPixelData: it reads getImageData at the element's CSS size from a
		// canvas pixel-ratio times larger, so on any high-density screen it returns only the top-left
		// corner - and no dimensions to interpret the bytes with.
		const canvas = await captureStable(() => library.toCanvas(node, resolvedOptions), c => pixelDigest(readPixels(c)), settings, 'toPixelData');
		const pixels = readPixels(canvas);
		const result = new Uint8Array(PIXEL_DATA_HEADER_BYTES + pixels.length);
		const header = new DataView(result.buffer);
		header.setUint32(0, canvas.width, true);
		header.setUint32(4, canvas.height, true);
		result.set(pixels, PIXEL_DATA_HEADER_BYTES);
		return result.buffer;
	}

	// Deliberately NOT library.toBlob. Upstream's toBlob forwards the options to toCanvas but then
	// calls its internal canvasToBlob(canvas) with no options at all, so `type` and `quality` are
	// silently dropped and every result is a PNG at quality 1 - asking it for a JPEG returns a PNG.
	// Doing the canvas-to-blob step here is what makes ToJpegBytesAsync and Quality actually work.
	// Upstreamed as https://github.com/bubkoo/html-to-image/pull/590; once a vendored release
	// contains it, this can call library.toBlob (through captureStable) instead.
	const canvas = await captureStable(() => library.toCanvas(node, resolvedOptions), c => c.toDataURL(), settings, 'toCanvas');
	const mimeType = format === 'jpeg' ? 'image/jpeg' : 'image/png';
	const quality = typeof resolvedOptions.quality === 'number' ? resolvedOptions.quality : 1;

	const blob = await new Promise(resolve => canvas.toBlob(resolve, mimeType, quality));
	if (!blob) {
		throw new Error(`The browser produced no ${mimeType} blob for the requested element.`);
	}

	return blob;
}

export async function getFontEmbedCss(target, options, errorListener) {
	const library = await getLibrary();
	const node = resolveNode(target);
	const settings = captureSettings(options);
	const fontCss = await withCaptureTimeout(library.getFontEmbedCSS(node, buildOptions(options, errorListener)), settings.captureTimeoutMs, 'getFontEmbedCSS');
	return asTextBlob(fontCss);
}

/**
 * Returns the live HTMLCanvasElement. Blazor marshals it as an IJSObjectReference, so the caller
 * owns it and must dispose - otherwise the canvas and its backing bitmap outlive the capture.
 */
export async function toCanvas(target, options, errorListener) {
	const library = await getLibrary();
	const node = resolveNode(target);
	const resolvedOptions = buildOptions(options, errorListener);
	const settings = captureSettings(options);
	return await captureStable(() => library.toCanvas(node, resolvedOptions), c => c.toDataURL(), settings, 'toCanvas');
}
