'use strict';

// Receives the live HTMLCanvasElement captured by ToCanvasAsync - Blazor revives the
// IJSObjectReference argument back into the original JS object - and attaches it to the story
// page. Showing the element is the simplest proof the result is a real canvas other JavaScript
// can consume, not an encoded copy. The DOM keeps the element after the interop reference is
// disposed on the .NET side.
window.demoAttachCanvas = (canvas, host) => {
	canvas.style.maxWidth = '320px';
	canvas.style.height = 'auto';
	host.replaceChildren(canvas);
};
