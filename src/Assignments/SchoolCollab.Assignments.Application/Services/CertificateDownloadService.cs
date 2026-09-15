using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace SchoolCollab.Assignments.Application.Services;

/// <summary>
/// C3 — thin browser-side orchestration for the certificate download
/// (decision (f)): fetches the PDF byte array via
/// <see cref="AssignmentsApiClient.GetCertificateAsync"/> then hands it to the
/// <c>fileDownload.js</c> module (a Blob + anchor-click save). The JS module is
/// loaded lazily via dynamic <c>import()</c>, cached on this instance, and
/// disposed here (the calling component disposes this service) — the
/// use-js-interop pattern (module ref owned by the consumer, disposed in a
/// <see cref="IAsyncDisposable"/> path, <see cref="JSDisconnectedException"/>-safe).
/// A 404 (no certificate) surfaces as <see cref="HttpRequestException"/> for
/// the UI to render an error bar rather than crash.
/// </summary>
public sealed class CertificateDownloadService(
    AssignmentsApiClient api,
    IJSRuntime js,
    ILogger<CertificateDownloadService> logger) : IAsyncDisposable
{
    private IJSObjectReference? _fileDownloadModule;

    /// <summary>Downloads the finalized certificate for a (assignment, ward)
    /// pair to the browser. Idempotent module load; <c>JSDisconnectedException</c>
    /// (the circuit closed mid-save) is swallowed harmlessly.</summary>
    public async Task DownloadAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
    {
        var pdf = await api.GetCertificateAsync(assignmentId, studentId, ct);

        try
        {
            _fileDownloadModule ??= await js.InvokeAsync<IJSObjectReference>(
                "import", "./_content/SchoolCollab.Assignments.Application/js/fileDownload.js");
            await _fileDownloadModule.InvokeVoidAsync("saveByteArray",
                $"certificate-{assignmentId:N}-{studentId:N}.pdf", pdf);
        }
        catch (JSDisconnectedException)
        {
            // The circuit closed before the save completed — nothing to surface.
        }

        logger.LogInformation(
            "Certificate downloaded for assignment {AssignmentId} / student {StudentId}",
            assignmentId, studentId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_fileDownloadModule is null) return;
        try
        {
            await _fileDownloadModule.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        _fileDownloadModule = null;
    }
}
