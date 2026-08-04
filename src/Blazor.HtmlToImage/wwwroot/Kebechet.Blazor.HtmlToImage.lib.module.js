// Blazor auto-discovers this file by name and runs the matching hook at startup, so a consuming
// app never adds a <script> tag. Both hooks are exported because which one fires depends on the
// hosting model (Web/WebAssembly/WebView).

const MARKER_ATTRIBUTE = 'data-kebechet-html-to-image';
const SCRIPT_PATH = '_content/Kebechet.Blazor.HtmlToImage/html-to-image.js';
const READY_PROMISE_KEY = '__kebechetHtmlToImageReady';

export function beforeStart() {
	injectLibrary();
}

export function beforeWebStart() {
	injectLibrary();
}

export function beforeWebAssemblyStart() {
	injectLibrary();
}

function injectLibrary() {
	if (globalThis[READY_PROMISE_KEY]) {
		return;
	}

	if (document.querySelector(`script[${MARKER_ATTRIBUTE}]`)) {
		return;
	}

	// The interop module awaits this instead of polling for globalThis.htmlToImage, so a capture
	// issued on a component's very first render still resolves rather than racing the download.
	globalThis[READY_PROMISE_KEY] = new Promise((resolve, reject) => {
		const script = document.createElement('script');
		script.src = SCRIPT_PATH;
		script.setAttribute(MARKER_ATTRIBUTE, '');
		// Excluded from captures taken by this very library - a <script> in the captured subtree
		// would otherwise be cloned into the output.
		script.classList.add('html-to-image-ignore');
		script.onload = () => resolve();
		script.onerror = () => reject(new Error(`Failed to load ${SCRIPT_PATH}.`));
		document.head.appendChild(script);
	});
}
