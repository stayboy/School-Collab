// WS-F3 (ar-15-signoff-relocation, decision (e)/step 5) — the Families host's copy of
// the C3 certificate-download interop. The Admin module lives in the Assignments
// Application RCL's wwwroot (served as _content/...); the Families host serves its own
// wwwroot at the app root, so the guardian sign page imports this copy. Loaded
// dynamically via IJSRuntime import() and cached on GuardianCertificateDownloadService,
// which owns the module reference. Saves a PDF byte array as a browser download.
//
// bUnit has no DOM download path, so this module is exercised by the UI tester; the
// bUnit tests assert the action gating + the client call instead. .NET Blazor Server
// round-trips a byte[] as a flat number array, so the bytes are wrapped in a Uint8Array
// before being fed to the Blob.
export function saveByteArray(fileName, bytes) {
    const byteArray = new Uint8Array(bytes);
    const blob = new Blob([byteArray], { type: 'application/pdf' });
    const url = URL.createObjectURL(blob);

    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);

    URL.revokeObjectURL(url);
}
