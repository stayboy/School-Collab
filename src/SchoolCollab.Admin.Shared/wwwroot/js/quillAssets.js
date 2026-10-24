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

// ── Placeholder-tag insertion ────────────────────────────────────────────────────────────────────────
//
// The tag menu is anchored to a button in the editor's own Quill toolbar, so there is no pointer to
// track and no right-click to capture: the token goes where the operator's caret already is. Every call
// is addressed by the EDITOR ROOT element's id — the component's own root, never the wrapper's
// `QuillToolBar`/`QuillEditor` ids, which repeat across the several editors one page can host (the
// assignment authoring page mounts more than one).

/// The Quill instance mounted inside an editor ROOT element, or null when the root is missing or Quill
/// has not been mounted inside it yet.
function findQuill(root) {
    if (!root) return null;

    const editorElement = root.querySelector('.ql-editor');
    if (!editorElement) return null;

    // The wrapper stores its Quill instance on the element it mounted into (`element.__quill = new
    // Quill(element, …)`), which is an ancestor of `.ql-editor` and a descendant of this root.
    for (let element = editorElement; element; element = element.parentElement) {
        const candidate = element.__quill;
        if (window.Quill && candidate instanceof window.Quill) {
            return candidate;
        }
        if (element === root) break;
    }

    return null;
}

/// Inserts `text` at the editor's CURRENT selection — the caret the operator last had in the body, which
/// Quill keeps as its saved range while the toolbar button has focus — then places the caret after the
/// token. With no selection at all it appends at the end of the document, so the button still does
/// something sensible in an empty body.
/// Returns false when there was nothing to insert into (no Quill under `rootId`, or empty `text`), so the
/// component does not treat a failed insert as a change and queue a preview of it.
export function insertPlaceholderAtSelection(rootId, text) {
    const quill = findQuill(document.getElementById(rootId));
    if (!quill || !text) return false;

    const selection = quill.getSelection();
    // getLength() counts the document's trailing newline, so the last insertable index is one before it.
    const index = selection ? selection.index : Math.max(0, quill.getLength() - 1);

    quill.insertText(index, text, 'user');
    quill.setSelection(index + text.length, 0);
    return true;
}
