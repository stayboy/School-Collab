// Recording materials (spec recording-materials D4/D5) — the ward player's media-object interop.
//
// A media instruction's staged file is fetched through the authenticated FamiliesApiClient (a raw
// <audio src>/<video src> cannot attach the bearer token the assignments-api requires), so the bytes
// arrive here as a .NET byte[] and are turned into a Blob object URL the player can bind to. A Blob
// URL is same-origin and seeks natively in-browser, so <audio>/<video> scrubbing works without any
// Range round-trip.
//
// Loaded dynamically via IJSRuntime import() and cached by the consuming component, which owns the
// module reference and revokes every URL it created on teardown (the use-js-interop pattern:
// IAsyncDisposable path, JSDisconnectedException-safe). .NET Blazor Server round-trips a byte[] as a
// flat number array, so it is wrapped in a Uint8Array before the Blob.

export function createMediaUrl(bytes, contentType) {
    const byteArray = new Uint8Array(bytes);
    const blob = new Blob([byteArray], { type: contentType || 'application/octet-stream' });
    return URL.createObjectURL(blob);
}

export function revokeMediaUrl(url) {
    if (url) {
        URL.revokeObjectURL(url);
    }
}
