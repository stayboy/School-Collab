using System.ClientModel;
using System.Net;

namespace SchoolCollab.AI.Services;

/// <summary>
/// Formats provider/transport exceptions into concise, user-facing messages,
/// mapping common HTTP statuses (401/403/429/5xx) to actionable guidance.
/// Extracted verbatim from <see cref="AIChatEngine"/>'s private
/// <c>FormatProviderError</c> helper so the assignment question-generation
/// endpoint can reuse the same friendly mapping (EC-8).
/// </summary>
internal static class ProviderErrorFormatter
{
    /// <summary>
    /// Returns a friendly message for a provider/transport exception.
    /// </summary>
    public static string Format(Exception ex)
    {
        var status = ex switch
        {
            ClientResultException cre => (int?)cre.Status,
            HttpRequestException hre => (int?)hre.StatusCode,
            _ => null
        };

        return status switch
        {
            401 or 403 => "The AI provider rejected the request as unauthorised. Please check that a valid OpenRouter API key is configured (OpenRouter:ApiKey).",
            429 => "The AI provider rate-limited the request. Please wait a moment and try again.",
            >= 500 => $"The AI provider returned a server error (HTTP {status}). Please try again in a moment.",
            _ => $"The AI chat could not be completed: {ex.Message}"
        };
    }
}
