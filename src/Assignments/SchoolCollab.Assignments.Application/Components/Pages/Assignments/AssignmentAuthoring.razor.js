// UX-7 (D2) — the browser-unload half of the assignment authoring page's unsaved-changes guard.
//
// An in-app navigation never reaches the browser (the Blazor-side location-changing handler
// cancels it and confirms with the shared FluentUI dialog), but a tab close, a reload or a URL
// typed into the address bar does. `window.beforeunload` is the only hook for those, and the
// platform forbids it from showing anything of its own: setting `preventDefault()` (or returning
// a non-empty string, in the legacy spelling) makes the browser raise ITS OWN leave-site prompt.
// That is why the two mechanisms coexist rather than one replacing the other.
//
// The module owns one listener, attached exactly while the .NET component reports unsaved work
// and detached as soon as the work is saved, reverted or discarded — and detached again on
// disposal, so a stale listener cannot guard a page that no longer exists. The dirty decision is
// made in .NET (a baseline comparison against the loaded state) and pushed across the boundary
// with register/unregister; no JavaScript callback into .NET is involved, because a synchronous
// browser event cannot await a Blazor Server round trip.

let dirtyRegistered = false;

function onBeforeUnload(event) {
    // Both spellings are required: `preventDefault()` is the standard one, and `returnValue` is
    // what Chrome/Firefox still need to raise the prompt at all.
    event.preventDefault();
    event.returnValue = '';
    return '';
}

export function registerBeforeUnload() {
    if (dirtyRegistered) {
        return;
    }

    window.addEventListener('beforeunload', onBeforeUnload);
    dirtyRegistered = true;
}

export function unregisterBeforeUnload() {
    if (!dirtyRegistered) {
        return;
    }

    window.removeEventListener('beforeunload', onBeforeUnload);
    dirtyRegistered = false;
}
