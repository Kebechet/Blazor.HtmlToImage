'use strict';

// Set by the JS initializer (Kebechet.Blazor.HtmlToImage.lib.module.js) before any component can
// render. Awaiting the script's own load event is the real readiness signal - polling for the
// global would still be a race on a slow device, only a noisier one.
const READY_PROMISE_KEY = '__kebechetHtmlToImageReady';

async function getLibrary() {
	const ready = globalThis[READY_PROMISE_KEY];
	if (ready) {
		await ready;
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
 * Blazor's interop serializer emits every unset nullable as JSON null. Passing those straight
 * through would override html-to-image's own defaults with null - so unset members are dropped
 * here rather than forwarded.
 */
function buildOptions(options) {
	const result = {};
	if (!options) {
		return result;
	}

	for (const [key, value] of Object.entries(options)) {
		if (value === null || value === undefined) {
			continue;
		}
		if (key === 'excludeCssClasses' || key === 'excludeSelector') {
			continue;
		}
		result[key] = value;
	}

	const filter = buildFilter(options.excludeCssClasses, options.excludeSelector);
	if (filter) {
		result.filter = filter;
	}

	return result;
}

export async function toDataUrl(target, format, options) {
	const library = await getLibrary();
	const node = resolveNode(target);
	const resolvedOptions = buildOptions(options);

	switch (format) {
		case 'png':
			return await library.toPng(node, resolvedOptions);
		case 'jpeg':
			return await library.toJpeg(node, resolvedOptions);
		case 'svg':
			return await library.toSvg(node, resolvedOptions);
		default:
			throw new Error(`Unsupported data-url format "${format}".`);
	}
}

/**
 * Returns the raw Blob / ArrayBuffer, NOT a DotNet.createJSStreamReference(...) wrapper.
 *
 * Because the .NET side declares the result as IJSStreamReference, Blazor's own result converter
 * calls createJSStreamReference on whatever this returns. Wrapping it here too means the framework
 * re-wraps an already-wrapped marker object, which is neither a Blob nor a typed array, and the
 * call dies with "Supplied value is not a typed array or blob."
 */
export async function toStream(target, format, options) {
	const library = await getLibrary();
	const node = resolveNode(target);
	const resolvedOptions = buildOptions(options);

	if (format === 'pixelData') {
		const pixels = await library.toPixelData(node, resolvedOptions);
		return pixels.buffer ?? pixels;
	}

	// Deliberately NOT library.toBlob. Upstream's toBlob forwards the options to toCanvas but then
	// calls its internal canvasToBlob(canvas) with no options at all, so `type` and `quality` are
	// silently dropped and every result is a PNG at quality 1 - asking it for a JPEG returns a PNG.
	// Doing the canvas-to-blob step here is what makes ToJpegBytesAsync and Quality actually work.
	const canvas = await library.toCanvas(node, resolvedOptions);
	const mimeType = format === 'jpeg' ? 'image/jpeg' : 'image/png';
	const quality = typeof resolvedOptions.quality === 'number' ? resolvedOptions.quality : 1;

	const blob = await new Promise(resolve => canvas.toBlob(resolve, mimeType, quality));
	if (!blob) {
		throw new Error(`The browser produced no ${mimeType} blob for the requested element.`);
	}

	return blob;
}

export async function getFontEmbedCss(target, options) {
	const library = await getLibrary();
	const node = resolveNode(target);
	return await library.getFontEmbedCSS(node, buildOptions(options));
}
