// MaterialEditDialog.razor.js — the dialog's collocated module (served from the RCL's static-web-asset
// root, `_content/SchoolCollab.Assignments.Application/…`, the AssignmentAuthoring.razor.js precedent).
//
// WHY THIS EXISTS (owner, 2026-10-10): a Text material's BODY is multi-line prose, so Enter inside it
// must never submit the dialog. FluentTextArea wraps the <fluent-text-area> web component and exposes
// no OnKeyDown EventCallback, and Blazor's static @onkeydown:preventDefault cannot be made conditional
// — the wall the AI chat hit, which is why this mirrors chatInput.js (SchoolCollab.AI.Chat).
//
// WHY THE FIRST ATTEMPT FAILED (owner-reported, 2026-10-10 — kept as the lesson):
//   It only called stopPropagation, on the theory that some ancestor was reading Enter as a submit.
//   Enter still submitted, because the submit is the FORM'S DEFAULT ACTION — the browser's implicit
//   submission — and a default action is not propagation: no amount of stopPropagation can reach it.
//   preventDefault() is the only lever, and it must run BEFORE the default is resolved, so the listener
//   is registered in the CAPTURE phase on the form (the earliest point in the event's path).
//   The price of preventDefault is the browser's own newline, so we insert it ourselves (chatInput.js's
//   insertNewlineAtCaret) and re-dispatch 'input' so Blazor's binding sees the new value.

/// <summary>
/// Resolves the editable textarea behind a FluentTextArea id: the id sits on the
/// <fluent-text-area> HOST element, while the editable surface is the <textarea> in its open shadow
/// DOM (the chatInput.js lookup).
/// </summary>
function innerTextarea(id) {
    const host = document.getElementById(id);
    if (!host) return null;
    if (host.tagName === 'TEXTAREA' || typeof host.selectionStart === 'number') {
        return host;
    }
    const sr = host.shadowRoot;
    if (sr) {
        return sr.querySelector('textarea')
            || sr.querySelector('[part="root"]')
            || sr.querySelector('[part="textarea"]')
            || null;
    }
    return null;
}

function insertNewlineAtCaret(id) {
    const el = innerTextarea(id);
    if (!el || typeof el.selectionStart !== 'number') return false;
    const start = el.selectionStart ?? el.value.length;
    const end = el.selectionEnd ?? start;
    el.value = el.value.slice(0, start) + '\n' + el.value.slice(end);
    el.selectionStart = el.selectionEnd = start + 1;
    // Notify Blazor's input binding, so the bound property updates and the caret survives the render.
    el.dispatchEvent(new Event('input', { bubbles: true }));
    return true;
}

/// <summary>
/// Makes Enter inside the body field a NEWLINE and never a submit. Idempotent: safe to call on every
/// render, so a re-rendered (or replaced) field re-arms the guard instead of silently losing it.
/// Returns false when there is nothing to guard, which the caller logs rather than swallows.
/// </summary>
export function guardEnterKey(id) {
    const host = document.getElementById(id);
    if (!host || host._materialEnterGuard) return false;
    if (!innerTextarea(id)) return false;

    const handler = (e) => {
        if (e.key !== 'Enter') return;
        // Enter belongs to the prose: kill the form's implicit submission, and keep the ancestor chain
        // out of it too (a JS submit path must not win either).
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();
        insertNewlineAtCaret(id);
    };

    // CAPTURE on the closest form (falling back to the host): the earliest point in the event's path,
    // so neither FluentUI's web component nor Blazor's EditForm can turn Enter into a submit first.
    (host.closest('form') ?? host).addEventListener('keydown', handler, true);
    host._materialEnterGuard = true;
    return true;
}
