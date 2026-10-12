// quillAssets.js — the rich instruction editor's asset loader (SchoolCollab.Admin.Shared).
//
// WHY THIS EXISTS (owner, 2026-10-11): the editor's assets must NOT be replicated in every host's
// App.razor — a third host would forget them, and the second copy is where copies drift. The component
// that needs them loads them: once, in order, each awaited before the next starts.
//
// ORDER MATTERS, and is the reason this is not three <script> tags in a component:
//   `quill.js` defines `window.Quill`, and the wrapper's `Blazored-BlazorQuill.js` assumes it is
//   already there. Component-level <script> tags do not promise load order; an awaited chain does.
//
// The assets are served by THIS library (`_content/SchoolCollab.Admin.Shared/...`), so every host that
// references Admin.Shared gets them — and hosts that never render the editor load nothing, because
// this module is imported lazily by the editor component alone.

const QUILL_CSS = '_content/SchoolCollab.Admin.Shared/lib/quill/quill.snow.css';
const QUILL_JS = '_content/SchoolCollab.Admin.Shared/lib/quill/quill.js';
const WRAPPER_SCRIPTS = [
    '_content/Blazored.TextEditor/quill-blot-formatter.min.js',
    '_content/Blazored.TextEditor/Blazored-BlazorQuill.js',
];

function ensureStylesheet(href) {
    if (document.querySelector(`link[href="${href}"]`)) return;
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = href;
    document.head.appendChild(link);
}

function loadScript(src) {
    return new Promise((resolve, reject) => {
        if (document.querySelector(`script[src="${src}"]`)) {
            resolve();
            return;
        }
        const script = document.createElement('script');
        script.src = src;
        script.onload = () => resolve();
        script.onerror = () => reject(new Error(`Could not load ${src}`));
        document.body.appendChild(script);
    });
}

/// Ensures Quill and the wrapper's scripts are present exactly once per page, in order.
/// Concurrent callers await the same promise, so two editors opened at once load one set.
export function ensureQuillAssets() {
    window.__quillAssetsPromise ??= (async () => {
        ensureStylesheet(QUILL_CSS);
        await loadScript(QUILL_JS);
        for (const src of WRAPPER_SCRIPTS) {
            await loadScript(src);
        }
        return true;
    })();

    return window.__quillAssetsPromise;
}
