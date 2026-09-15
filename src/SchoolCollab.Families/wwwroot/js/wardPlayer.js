// F1 (slice 2b) — ward-player video progress heartbeat. The app's second JS
// interop module (after fileDownload.js, ar-11), following the same
// use-js-interop pattern: loaded via dynamic import(), the module reference is
// cached and disposed by the consuming component (Ward/Assignment.razor), and
// every await is JSDisconnectedException-safe.
//
// attachVideoHeartbeats(dotNetRef) finds the rendered <video data-module-id>
// elements, registers a throttled ~1 tick/10s timeupdate listener on each, and
// reports the nearest 10% via the .NET [JSInvokable] "onVideoTick". Blazor
// Server has no real DOM media events of its own, so the raw <video> element
// is used (a plain element, not a FluentUI web component, for video).
//
// detachVideoHeartbeats() removes every registered listener and clears the
// registry, so the consuming component's DisposeAsync can tear the watchers
// down (and the module reference disposed) without leaking listeners on the
// live <video> elements.

const heartbeatEntries = new Map(); // video element -> { handler, lastReported }

export function attachVideoHeartbeats(dotNetRef) {
    const videos = document.querySelectorAll('video.ward-video');

    videos.forEach((video) => {
        const moduleId = video.dataset.moduleId;
        if (!moduleId || heartbeatEntries.has(video)) return;

        const entry = { lastReported: -1, handler: null };
        entry.handler = () => {
            if (video.duration === 0 || Number.isNaN(video.duration)) return;
            const percent = Math.min(100, Math.round((video.currentTime / video.duration) * 100));
            // Report at 10% granularity; throttled by construction — a tick only
            // fires when the 10% bucket advances (~each tenth of the video).
            const bucket = Math.floor(percent / 10) * 10;
            if (bucket <= entry.lastReported) return;
            entry.lastReported = bucket;
            dotNetRef.invokeMethodAsync('onVideoTick', moduleId, bucket)
                .catch((err) => console.warn('wardPlayer heartbeat failed', err));
        };
        video.addEventListener('timeupdate', entry.handler);
        heartbeatEntries.set(video, entry);
    });
}

export function detachVideoHeartbeats() {
    heartbeatEntries.forEach((entry, video) => {
        video.removeEventListener('timeupdate', entry.handler);
    });
    heartbeatEntries.clear();
}
