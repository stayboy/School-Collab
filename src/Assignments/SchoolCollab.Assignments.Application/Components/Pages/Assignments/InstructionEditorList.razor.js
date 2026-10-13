// Owner pill rework (2026-10-12): the list's tiny media module — revokes the session-held Blob URLs of
// recorded takes when their row goes away (a discarded take must not leak memory). The revoke half of
// the ward's mediaPlayer.js precedent; the list owns the module reference and disposes it.
export function revokeMediaUrl(url) {
    if (url) {
        URL.revokeObjectURL(url);
    }
}