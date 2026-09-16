using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace SchoolCollab.Families.Services;

/// <summary>
/// WS-F3 (ar-15-signoff-relocation, decision (e)/step 5) — the Families-side mirror of the
/// Admin C3 <c>CertificateDownloadService</c>: fetch the finalized certificate PDF through
/// <see cref="FamiliesApiClient.GetGuardianCertificateAsync"/> (a plain <c>&lt;a href&gt;</c>
/// cannot attach the <c>x-deeplink-token</c> header) and hand the bytes to the host's
/// <c>fileDownload.js</c> module (a Blob + anchor-click save). The module reference is loaded
/// lazily, owned by this instance, and disposed here (the calling component disposes this
/// service) — the use-js-interop pattern: <see cref="IAsyncDisposable"/> path,
/// <see cref="JSDisconnectedException"/>-safe. A 404 surfaces as
/// <see cref="HttpRequestException"/> for the UI to render an error bar rather than crash.
/// </summary>
public sealed class GuardianCertificateDownloadService(
    FamiliesApiClient api,
    IJSRuntime js,
    ILogger<GuardianCertificateDownloadService> logger) : IAsyncDisposable
{
    private IJSObjectReference? _fileDownloadModule;

    /// <summary>Downloads the finalized certificate for a (assignment, ward) pair to the
    /// browser, authenticated by the re-minted guardian header token. Idempotent module
    /// load; <see cref="JSDisconnectedException"/> (the circuit closed mid-save) is
    /// swallowed harmlessly.</summary>
    public async Task DownloadAsync(
        Guid assignmentId, Guid studentId, GuardianCallerContext caller, CancellationToken ct = default)
    {
        var pdf = await api.GetGuardianCertificateAsync(assignmentId, studentId, caller, ct);

        try
        {
            _fileDownloadModule ??= await js.InvokeAsync<IJSObjectReference>(
                "import", "./js/fileDownload.js");
            await _fileDownloadModule.InvokeVoidAsync("saveByteArray",
                $"certificate-{assignmentId:N}-{studentId:N}.pdf", pdf);
        }
        catch (JSDisconnectedException)
        {
            // The circuit closed before the save completed — nothing to surface.
        }

        logger.LogInformation(
            "Guardian certificate downloaded for assignment {AssignmentId} / student {StudentId}",
            assignmentId, studentId);
    }

    /// <inheritdoc/>
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
