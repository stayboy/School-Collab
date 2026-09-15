namespace SchoolCollab.Assignments.Application.Services;

/// <summary>
/// WS-B2 (spec §3.4 / decision g) — extracts readable text from a URL so the
/// AI question wizard can pass it to the generator as reference material
/// (<c>QuestionGenerationRequest.ResourceTexts</c>). Fail-open: an unsupported
/// scheme, non-success status, timeout or network failure yields
/// <see cref="UrlTextExtractionResult.Success"/> == <see langword="false"/> with a
/// friendly error, and the wizard generates without that URL.
/// </summary>
public interface IUrlTextExtractor
{
    Task<UrlTextExtractionResult> ExtractAsync(string url, CancellationToken ct = default);
}

/// <summary>The outcome of a single URL extraction.</summary>
public sealed record UrlTextExtractionResult(bool Success, string? Text, string? Error);
