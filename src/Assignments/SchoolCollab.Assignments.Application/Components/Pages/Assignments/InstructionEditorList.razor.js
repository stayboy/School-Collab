// Owner pill rework (2026-10-12): the list's media module — revokes the session-held Blob URLs of
// recorded takes when their row goes away (a discarded take must not leak memory). Round
// media-pill-listing (Q8/Q7) widened it with the served-playback stream→Blob-URL helper and the
// upload duration probe.
//
// The duration probe stays SINGLE-LOCATION in RecorderPanel.razor.js (mediaDuration, consumed by the
// recorder's own StopAsync): this module, the page and the recorder are all consumers of the one
// export — three consumers, one implementation. Importing the sibling collocated module keeps that
// true without a shared wwwroot/js/ file.
import { mediaDuration } from './RecorderPanel.razor.js';

// Re-exported so the list itself can probe a served/preview Blob URL for display only — still the one
// implementation in the recorder module (three consumers, one function).
export { mediaDuration };

export function revokeMediaUrl(url) {
    if (url) {
        URL.revokeObjectURL(url);
    }
}

// Round media-pill-listing (Q8): the served bytes arrive as a DotNetStreamReference — never a byte[]
// (a .NET→JS byte[] round-trips as an oversized number array in Blazor Server, and the JS→.NET
// direction is already capped at a 32 KB single array). The stream is read here and wrapped in a
// same-origin Blob URL, which seeks natively without Range round-trips.
export async function createMediaUrlFromStream(streamRef, contentType) {
    const buffer = await streamRef.arrayBuffer();
    const blob = new Blob([buffer], { type: contentType || 'application/octet-stream' });
    return URL.createObjectURL(blob);
}

// Round media-pill-listing (Q7): best-effort duration probe for a just-staged upload, reading the
// File still attached to the real <input type=file> inside `selector`. Any failure (no input, no
// file, no metadata) resolves null so the readout falls back to "—:—" and staging is unaffected.
// The probe itself is the recorder module's own mediaDuration — this is only the file→URL plumbing.
export async function probeDurationFromInput(selector) {
    const input = document.querySelector(selector);
    const file = input && input.files && input.files.length > 0 ? input.files[0] : null;
    if (!file) {
        return null;
    }

    const url = URL.createObjectURL(file);
    try {
        return await mediaDuration(url);
    } finally {
        URL.revokeObjectURL(url);
    }
}
