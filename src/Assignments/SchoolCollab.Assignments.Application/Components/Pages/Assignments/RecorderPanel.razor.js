// Recording materials (spec recording-materials D3/D7/D8/D9) — the browser-capture interop for the
// inline recorder. Both hosts are Blazor Server (InteractiveServerRenderMode, prerender:false), so
// getUserMedia/MediaRecorder run entirely in the browser; this module is the only thing that touches
// them. Loaded dynamically via IJSRuntime import(), cached by the consuming component, which owns the
// reference and calls stopCapture() + dispose on teardown so the camera/mic light goes off (AC-10).
//
// Design (owner 2026-10-12):
//   - D8 capability probe (isSupported) gates the UI: getUserMedia needs a secure context AND the API.
//   - D9 no bitrate/resolution constraint — the browser's default MediaRecorder output is accepted
//     (webm/opus in Chromium, mp4 in Safari); both are already in the upload allow-list.
//   - D7 byte-stop: the recorder auto-stops at ~95 MiB so a take is never lost to a server 413; the
//     raised server cap remains the real ceiling.
//   - The take stays in the browser as a Blob and is delivered to .NET by writing it into a hidden
//     <input type=file> (commitBlob), which Blazor's InputFile STREAMS over the circuit in bounded
//     chunks. Never marshal it as one byte[] JS→.NET interop call — Blazor Server blocks a lone large
//     byte-array transfer (RemoteJSRuntime's MaximumReceiveMessageSize gate; dotnet/aspnetcore#38941).

let activeStream = null;
let activeRecorder = null;
let recordedChunks = [];
let autoStopTimer = null;
// The most recent take, held in the browser until delivered (commitBlob) or discarded (clearBlob).
// Never marshalled to .NET as bytes — see endRecording's note.
let lastBlob = null;

// D8: getUserMedia requires a secure context (https or localhost) and the API. When either is absent
// the UI disables the Record affordance and points at Upload From Device rather than dead-ending.
export function isSupported() {
    return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia && window.isSecureContext);
}

// Requests mic (audio) or mic+camera (video) access and binds the live stream to the preview element
// so the author sees themselves before recording. Resolves to the chosen MediaRecorder mime type (or
// '' when the browser picks its default); rejects with a coded reason the component maps to a message:
//   'denied' (permission refused) · 'notfound' (no device) · 'unsupported' (no usable codec) ·
//   'insecure' (not a secure context).
export async function startCapture(previewElementId, wantVideo, autoStopBytes) {
    stopCapture();
    lastBlob = null; // a new take supersedes any stale one awaiting commit

    if (!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia)) {
        throw new Error('insecure');
    }

    let stream;
    try {
        stream = await navigator.mediaDevices.getUserMedia({
            audio: true,
            video: wantVideo ? { width: { ideal: 1280 }, height: { ideal: 720 } } : false
        });
    } catch (err) {
        if (err && (err.name === 'NotAllowedError' || err.name === 'SecurityError')) {
            throw new Error('denied');
        }
        if (err && (err.name === 'NotFoundError' || err.name === 'OverconstrainedError')) {
            throw new Error('notfound');
        }
        throw new Error('denied');
    }
    activeStream = stream;

    const preview = previewElementId ? document.getElementById(previewElementId) : null;
    if (preview) {
        preview.srcObject = stream;
        if (wantVideo && typeof preview.play === 'function') {
            preview.play().catch(() => { /* autoplay can be blocked; the preview is cosmetic */ });
        }
    }

    // D9: prefer a broadly-playable container, fall back to the browser default. mp4 first (Safari),
    // then webm (Chromium/Firefox); MediaRecorder picks its default when neither is supported.
    const candidates = wantVideo
        ? ['video/mp4;codecs=avc1.42E01E,mp4a.40.2', 'video/mp4', 'video/webm;codecs=vp8,opus', 'video/webm']
        : ['audio/mp4', 'audio/webm;codecs=opus', 'audio/webm'];
    let options = {};
    if (typeof MediaRecorder !== 'undefined' && MediaRecorder.isTypeSupported) {
        const supported = candidates.find((t) => MediaRecorder.isTypeSupported(t));
        if (supported) {
            options = { mimeType: supported };
        }
    }

    try {
        activeRecorder = new MediaRecorder(stream, options);
    } catch (err) {
        stopCapture();
        throw new Error('unsupported');
    }
    recordedChunks = [];

    activeRecorder.ondataavailable = (event) => {
        if (event.data && event.data.size > 0) {
            recordedChunks.push(event.data);
        }
    };

    // D7: a wall-clock safety net only — the real guard is the byte size checked per chunk. Kept
    // coarse (checked per chunk) so it never stops a normal take early.
    if (autoStopBytes && autoStopBytes > 0) {
        autoStopTimer = setInterval(() => {
            const size = recordedChunks.reduce((sum, chunk) => sum + chunk.size, 0);
            if (size >= autoStopBytes && activeRecorder && activeRecorder.state === 'recording') {
                activeRecorder.stop();
            }
        }, 1000);
    }

    return options.mimeType || '';
}


// Begins recording (timeslice 1s so chunks accumulate for the byte-stop check).
export function beginRecording() {
    if (!activeRecorder) {
        throw new Error('unsupported');
    }
    activeRecorder.start(1000);
}

// Stops recording and finalises the take into a module-held Blob. Returns only METADATA — never the
// bytes. Marshalling the whole recording as one byte[] JS→.NET interop call is BLOCKED in Blazor
// Server (RemoteJSRuntime caps a single byte-array transfer at the hub's MaximumReceiveMessageSize;
// see dotnet/aspnetcore#38941 — the sanctioned pattern is to stream, not marshal). The Blob stays in
// the browser and is delivered by writing it into a real <input type=file> (commitBlob), which
// Blazor's InputFile streams over the circuit in bounded chunks — the SAME path FluentInputFile
// already uses for the repo's 100MiB uploads. The device tracks are released here so the camera/mic
// light goes off the moment a take is captured (AC-10).
export async function endRecording(wantVideo) {
    if (!activeRecorder) {
        throw new Error('unsupported');
    }
    const recorder = activeRecorder;
    const mimeType = recorder.mimeType || (wantVideo ? 'video/webm' : 'audio/webm');

    lastBlob = await new Promise((resolve) => {
        recorder.onstop = () => {
            resolve(new Blob(recordedChunks, { type: mimeType }));
        };
        if (recorder.state !== 'inactive') {
            recorder.stop();
        } else {
            resolve(new Blob(recordedChunks, { type: mimeType }));
        }
    });

    const extension = mimeType.includes('mp4') ? 'mp4' : 'webm';
    const fileName = `recording-${Date.now()}.${extension}`;

    stopCapture();
    return { contentType: mimeType, fileName, size: lastBlob.size };
}

// AC-3 preview: a Blob object URL so the author plays the take back BEFORE committing (retake stays a
// first-class action). Built straight from the module-held Blob — no bytes cross interop. The
// component revokes the URL on reset/dispose so a discarded take never leaks memory.
export function createPreviewUrl() {
    if (!lastBlob) {
        throw new Error('unsupported');
    }
    return URL.createObjectURL(lastBlob);
}

// Delivery (D3): wrap the held Blob as a File and drop it into the target <input type=file>, then
// dispatch change — the EXACT technique FluentInputFile's own initializeFileDropZone uses. .NET's
// InputFile OnChange fires and streams the content through IBrowserFile.OpenReadStream (bounded
// chunks), so the recording never crosses the circuit as a single oversized byte[].
export function commitBlob(inputSelector, fileName) {
    if (!lastBlob) {
        throw new Error('unsupported');
    }
    const input = document.querySelector(inputSelector);
    if (!input) {
        throw new Error('unsupported');
    }
    const file = new File([lastBlob], fileName, { type: lastBlob.type });
    const transfer = new DataTransfer();
    transfer.items.add(file);
    input.files = transfer.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
    // Keep lastBlob: the File references the same underlying data (no copy), and holding it lets the
    // author RETRY "Use this recording" if staging is refused. clearBlob/next start release it.
}

export function revokePreviewUrl(url) {
    if (url) {
        URL.revokeObjectURL(url);
    }
}

// Releases the live stream and clears the recorder + auto-stop timer. Idempotent; safe to call from
// dispose. Never throws (a circuit already gone must not break teardown). Does NOT touch lastBlob —
// a take may be awaiting commit — commitBlob/clearBlob own that.
export function stopCapture() {
    if (autoStopTimer) {
        clearInterval(autoStopTimer);
        autoStopTimer = null;
    }
    if (activeStream) {
        activeStream.getTracks().forEach((track) => track.stop());
        activeStream = null;
    }
    if (activeRecorder) {
        try { activeRecorder.onstop = null; activeRecorder.ondataavailable = null; } catch { /* noop */ }
        activeRecorder = null;
    }
    recordedChunks = [];
}

// Drops any held take (new recording, retake, dispose) so its memory is released.
export function clearBlob() {
    lastBlob = null;
}
