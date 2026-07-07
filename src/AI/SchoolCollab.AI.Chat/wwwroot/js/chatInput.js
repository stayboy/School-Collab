// chatInput.js — JS interop helper for the AI chat input textarea.
//
// Loaded lazily by AiChat via the standard Blazor "import" pattern
// (JS.InvokeAsync<IJSObjectReference>("import", "./_content/.../chatInput.js")).
// The module is served as a static asset of the SchoolCollab.AI.Chat RCL —
// there is no <script> tag in App.razor for it.
//
// Why a JS listener instead of @onkeydown on FluentTextArea?
//   FluentTextArea wraps the <fluent-text-area> web component and exposes no
//   OnKeyDown EventCallback parameter, so a Blazor @onkeydown handler can't
//   be wired reliably, and conditional preventDefault (suppress the newline
//   only on plain Enter, or the caret move only when navigating history) isn't
//   possible with the static @onkeydown:preventDefault directive. A native
//   keydown listener gives us full control over preventDefault per key and
//   hands the action off to .NET via a DotNetObjectReference.

/// <summary>
/// Resolves the editable textarea for a given AiChat input id.
/// FluentTextArea puts the id on the <fluent-text-area> host element; the
/// actual editable surface is the <textarea> inside its (open) shadow DOM.
/// For a plain native textarea (id directly on it) the element itself is
/// returned. Returns null if nothing editable can be found.
/// </summary>
function innerTextarea(id) {
    const host = document.getElementById(id);
    if (!host) return null;
    // Native textarea (id is on the element itself).
    if (host.tagName === 'TEXTAREA' || typeof host.selectionStart === 'number') {
        return host;
    }
    // Web component: editable surface lives in the open shadow DOM.
    const sr = host.shadowRoot;
    if (sr) {
        return sr.querySelector('textarea')
            || sr.querySelector('[part="root"]')
            || sr.querySelector('[part="textarea"]')
            || null;
    }
    return null;
}

export function getTextAreaSelection(id) {
    const el = innerTextarea(id);
    if (!el || typeof el.selectionStart !== 'number') {
        return { start: -1, end: -1, length: -1 };
    }
    return {
        start: el.selectionStart,
        end: el.selectionEnd,
        length: el.value.length,
    };
}

export function insertNewlineAtCaret(id) {
    const el = innerTextarea(id);
    if (!el) return false;
    const start = el.selectionStart ?? el.value.length;
    const end = el.selectionEnd ?? start;
    const value = el.value;
    el.value = value.slice(0, start) + '\n' + value.slice(end);
    const caret = start + 1;
    el.selectionStart = el.selectionEnd = caret;
    // Notify Blazor's input binding so the bound property updates and the
    // caret stays put on the next render. The 'input' event is what
    // FluentTextArea listens for via Blazor's oninput; dispatching it
    // picks up the new value.
    el.dispatchEvent(new Event('input', { bubbles: true }));
    return true;
}

/// <summary>
/// Attaches a keydown listener to the chat input so Enter submits, Ctrl+Enter
/// inserts a newline, and ArrowUp/ArrowDown walk prompt history — with
/// preventDefault applied only where we act, so normal typing and caret
/// movement are never disturbed. Actions are forwarded to the supplied
/// DotNetObjectReference via JSInvokable methods.
/// </summary>
export function attachKeydownHandler(id, dotNetRef) {
    const host = document.getElementById(id);
    if (!host || host._chatKeydown) return false;

    const handler = (e) => {
        const key = e.key;

        if (key === 'Enter') {
            // Ctrl+Enter inserts a newline at the caret (plain Enter is
            // submit, Shift+Enter keeps the textarea's default newline).
            if (e.ctrlKey && !e.metaKey && !e.altKey) {
                e.preventDefault();
                insertNewlineAtCaret(id);
                return;
            }
            // Plain Enter (no Shift, no Ctrl, no Alt, no Meta) submits.
            if (!e.shiftKey && !e.ctrlKey && !e.altKey && !e.metaKey) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('SubmitFromKeyAsync');
                return;
            }
            // Any other Enter combination (e.g. Shift+Enter) falls through
            // to the textarea's default newline behaviour.
            return;
        }

        if (key === 'ArrowUp' || key === 'ArrowDown') {
            const el = innerTextarea(id);
            if (!el) return;
            const start = el.selectionStart ?? 0;
            const end = el.selectionEnd ?? start;
            const len = (el.value ?? '').length;
            const isEmpty = len === 0;

            // Only navigate history from the top edge (Up) / bottom edge
            // (Down), or when the input is empty — otherwise leave the
            // caret move to the textarea so in-draft editing works.
            const allowed = isEmpty
                || (key === 'ArrowUp' && start === 0)
                || (key === 'ArrowDown' && end === len);

            if (allowed) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync(
                    'NavigateHistoryFromKeyAsync',
                    key === 'ArrowUp' ? -1 : 1);
            }
        }
    };

    host._chatKeydown = handler;
    // Listen on the host element; keydown bubbles (composed) out of the
    // shadow-DOM textarea, so this catches every keypress inside it.
    host.addEventListener('keydown', handler);
    return true;
}

export function detachKeydownHandler(id) {
    const host = document.getElementById(id);
    if (host && host._chatKeydown) {
        host.removeEventListener('keydown', host._chatKeydown);
        delete host._chatKeydown;
    }
}