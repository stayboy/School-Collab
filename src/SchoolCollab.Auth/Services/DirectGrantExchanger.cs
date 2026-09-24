using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SchoolCollab.Auth.Options;

namespace SchoolCollab.Auth.Services;

/// <summary>Outcome of a Direct-Grant exchange (spec §5.2 / D6).</summary>
public enum DirectGrantStatus
{
    Success,
    InvalidCredentials,
    DisabledUser,
    KeycloakUnreachable,
}

/// <summary>
/// Result of <see cref="DirectGrantExchanger.ExchangeAsync"/>. The tokens are
/// present only on <see cref="IsSuccess"/> and must be handed to the session
/// custody store — they never appear in an HTTP response body, a log line or an
/// exception message (D7 / AC9).
/// </summary>
public sealed record DirectGrantResult(
    DirectGrantStatus Status,
    string? AccessToken = null,
    string? RefreshToken = null,
    string? IdToken = null,
    int ExpiresInSeconds = 0,
    string? Detail = null)
{
    public bool IsSuccess => Status == DirectGrantStatus.Success;

    /// <summary>Builds a success result from the token endpoint's 200 JSON payload.</summary>
    public static DirectGrantResult SuccessFromTokenJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var accessToken = GetString(root, "access_token");
            if (accessToken is null)
            {
                // A 200 without an access token is a malformed endpoint, not a
                // credential failure: fail toward "unreachable" rather than
                // letting an empty token be stored.
                return new DirectGrantResult(DirectGrantStatus.KeycloakUnreachable,
                    Detail: "the token endpoint returned 200 with no access_token");
            }

            return new DirectGrantResult(
                DirectGrantStatus.Success,
                AccessToken: accessToken,
                RefreshToken: GetString(root, "refresh_token"),
                IdToken: GetString(root, "id_token"),
                ExpiresInSeconds: GetInt(root, "expires_in"));
        }
        catch (JsonException)
        {
            return new DirectGrantResult(DirectGrantStatus.KeycloakUnreachable,
                Detail: "the token endpoint returned a non-JSON 200 response");
        }
    }

    /// <summary>
    /// Classifies a non-success token response. Credential failures are the 400/401
    /// family; everything else (5xx, redirects, other errors) fails toward
    /// <see cref="DirectGrantStatus.KeycloakUnreachable"/> so a healthy caller never
    /// mistakes an endpoint outage for a bad password.
    /// </summary>
    public static DirectGrantResult ClassifyFailure(HttpStatusCode status, string body)
    {
        var description = TryGetErrorDescription(body);
        if (description is null)
        {
            // BOUNDED fallback (pass-3c P2-4): when error_description is absent or the body is
            // not JSON, do NOT reflect the raw upstream body to the caller — a proxy error page
            // could echo far more than a diagnostic (and could carry attacker-influenced text
            // into a later response). A short static message keeps the classification without
            // the reflection.
            description =
                "the token endpoint returned an error response without a parseable error_description";
        }

        if (status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            return ContainsAccountDisabled(description)
                ? new DirectGrantResult(DirectGrantStatus.DisabledUser, Detail: description)
                : new DirectGrantResult(DirectGrantStatus.InvalidCredentials, Detail: description);
        }

        return new DirectGrantResult(DirectGrantStatus.KeycloakUnreachable, Detail: description);
    }

    /// <summary>Convenience for transport failures (no HTTP response was received).</summary>
    public static DirectGrantResult Unreachable() => new(DirectGrantStatus.KeycloakUnreachable);

    private static bool ContainsAccountDisabled(string description)
        // Broad match is deliberate (P1 fix): Keycloak 26.4's canonical disabled-account
        // error_description is "Account is disabled, contact your administrator."
        // (accountDisabledMessage) — it does NOT contain the substring "account disabled"
        // because of the intervening "is ", so a narrower match misclassifies a disabled
        // user as InvalidCredentials. The "is not fully set up" phrase catches the
        // not-fully-provisioned variant. The brute-force lockout message
        // ("Invalid username or password.") contains no "disabled" and must stay classified
        // as InvalidCredentials deliberately (no user enumeration).
        => description.Contains("disabled", StringComparison.OrdinalIgnoreCase)
           || description.Contains("is not fully set up", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetErrorDescription(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error_description", out var description)
                ? description.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static int GetInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
}

/// <summary>
/// Performs the OAuth2 Resource Owner Password Credentials (Direct Grant) exchange
/// against Keycloak's token endpoint (spec §5.2 / D6). The confidential client
/// secret stays server-side for the lifetime of the request; the returned token
/// set is handed to the session custody store, never to a response body.
/// </summary>
public sealed class DirectGrantExchanger
{
    private readonly HttpClient _httpClient;
    private readonly AuthServiceOptions _options;

    public DirectGrantExchanger(HttpClient httpClient, IOptions<AuthServiceOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<DirectGrantResult> ExchangeAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || password is null)
        {
            // No exchange is attempted for a blank credential pair: fail the same
            // way Keycloak would, without ever sending an empty password.
            return new DirectGrantResult(DirectGrantStatus.InvalidCredentials,
                Detail: "username or password was blank");
        }

        var endpoint = new Uri($"{_options.Keycloak.Authority.TrimEnd('/')}/protocol/openid-connect/token");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["client_id"] = _options.Keycloak.ClientId,
            ["client_secret"] = _options.Keycloak.ClientSecret,
            ["username"] = username,
            ["password"] = password,
        });

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(endpoint, form, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Network failure / TLS / DNS: the endpoint did not answer at all.
            return DirectGrantResult.Unreachable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation is not a Keycloak failure — rethrow, never swallow.
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient timeout: the token endpoint did not answer in time.
            return DirectGrantResult.Unreachable();
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return response.IsSuccessStatusCode
                ? DirectGrantResult.SuccessFromTokenJson(body)
                : DirectGrantResult.ClassifyFailure(response.StatusCode, body);
        }
    }
}
