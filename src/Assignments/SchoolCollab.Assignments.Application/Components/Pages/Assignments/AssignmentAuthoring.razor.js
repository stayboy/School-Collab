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

// Owner, 2026-10-10 (round content-materials-redesign, Q3) — the kebab's "Upload From Device" pops the
// file picker DIRECTLY, with no dialog in between.
//
// FluentInputFile owns its own hidden <input type="file"> and exposes no API to open it, so this
// reaches the one it rendered inside the dropzone and clicks it. The selector is pinned by the
// component (`#authoring-materials-dropzone`) and the bUnit suite asserts that the action makes this
// call — so the deliberate departure from the "no JS, no hidden-element click" note in
// ResourcesSection stays visible rather than becoming a silent habit.
export function openMaterialFilePicker(dropzoneSelector) {
    const input = document.querySelector(`${dropzoneSelector} input[type=file]`);

    if (input) {
        input.click();
    }
}
