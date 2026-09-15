// C3 certificate download (the app's first JS interop — decision (f)).
// Loaded dynamically via IJSRuntime import() and cached on the calling
// component (ModuleServices registers CertificateDownloadService, which owns
// this module). Saves a PDF byte array as a browser download.
//
// bUnit has no DOM download path, so this module is exercised manually / by
// the UI tester; the bUnit tests assert the action gating + the ApiClient call
// instead. .NET Blazor Server round-trips a byte[] as a flat number array, so
// the bytes are wrapped in a Uint8Array before being fed to the Blob.
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
