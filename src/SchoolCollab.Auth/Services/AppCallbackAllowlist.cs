using Microsoft.Extensions.Options;
using SchoolCollab.Auth.Options;

namespace SchoolCollab.Auth.Services;

/// <summary>
/// Fail-closed matcher for the per-app callback allowlist (spec §14, plan-review P1-3). A
/// one-time handshake code is minted only for a redirect target that matches a configured
/// <b>exact prefix</b>: identical scheme, identical host, identical port, and a path that is
/// either identical to the configured path or extends it at a <c>/</c> segment boundary
/// (<c>/signin-handshake</c> and <c>/signin-handshake/continuation</c> match; the
/// suffix-confusion spellings <c>/signin-handshake-evil</c> and <c>/signin-handshakeX</c> do
/// not). The query string and fragment are ignored — the Blazor hosts mint
/// <c>{scheme}://{host}{pathBase}/signin-handshake?ReturnUrl=&lt;escaped&gt;</c>
/// (<see cref="SchoolCollab.Core.Auth.PortalRedirectChallengeHandler"/> and B4's
/// <c>BuildCallbackUri</c>), so the nested <c>ReturnUrl</c> must not break the match while the
/// destination's scheme/host/port/path stay pinned.
/// <para>
/// The comparison is component-wise on a parsed <see cref="Uri"/>, never a substring or raw
/// string-prefix test on the caller's text: <c>http://evil.example/?next=http://localhost:5300/signin-handshake</c>
/// contains an allowlisted prefix but reaches an attacker host, and must be rejected. A malformed
/// candidate (relative, unparseable, non-http(s), hostless, carrying userinfo the browser would
/// hide, or spelled with a <c>.</c>/<c>..</c> path segment) and a malformed allowlist entry both
/// match nothing — malformed entries are skipped rather than fatal, so one typo narrows the list
/// instead of widening it. The dot-segment refusal is the one rule read off the caller's text
/// rather than the parsed components, because parsing has already resolved those segments away
/// (see <see cref="HasDotSegment"/>) — one control, one contract, mirrored by the portal's
/// <c>_split_uri</c>.
/// </para>
/// <para>
/// <b>Default deny:</b> with no usable entry (empty, blank or all-malformed configuration) every
/// candidate is rejected. The allowlist can never fail open; the accompanying
/// <see cref="AuthServiceOptions.FirstValidationError"/> makes an empty configured value a startup
/// failure so even that closed state is not reachable by an unconfigured deployment.
/// </para>
/// </summary>
public sealed class AppCallbackAllowlist
{
    private readonly IReadOnlyList<AllowedPrefix> _prefixes;

    /// <summary>Binds the allowlist from <c>Auth:AppCallbackPrefixes</c> (DI path).</summary>
    public AppCallbackAllowlist(IOptions<AuthServiceOptions> options)
        : this(ConfiguredValue(options))
    {
    }

    /// <summary>Builds the matcher from an already-resolved configuration value — the shape the
    /// tests compose, so the matcher's own semantics are covered without an options binder.</summary>
    public AppCallbackAllowlist(string? configuredPrefixes)
    {
        _prefixes = Parse(configuredPrefixes);
    }

    /// <summary>
    /// Whether <paramref name="redirectUri"/> may receive a one-time code: an absolute http(s) URI
    /// without userinfo whose scheme, host and port equal a configured prefix and whose path is
    /// that prefix's path exactly or a <c>/</c>-delimited extension of it. Anything else —
    /// including <c>null</c>, a relative path, a dot-segment spelling, an unparseable value or an
    /// empty allowlist — returns <c>false</c>.
    /// </summary>
    public bool IsAllowed(string? redirectUri)
    {
        if (!Uri.TryCreate(redirectUri?.Trim(), UriKind.Absolute, out var candidate)
            || !IsHttpScheme(candidate.Scheme)
            || candidate.Host.Length == 0
            || candidate.UserInfo.Length > 0
            || HasDotSegment(candidate.OriginalString))
        {
            return false;
        }

        foreach (var prefix in _prefixes)
        {
            if (string.Equals(prefix.Scheme, candidate.Scheme, StringComparison.Ordinal)
                && string.Equals(prefix.Host, candidate.Host, StringComparison.OrdinalIgnoreCase)
                && prefix.Port == candidate.Port
                && MatchesPath(prefix.Path, candidate.AbsolutePath))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the configured list, rejecting a missing options instance rather than
    /// silently building an empty (deny-everything) allowlist from it.</summary>
    private static string? ConfiguredValue(IOptions<AuthServiceOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Value.AppCallbackPrefixes;
    }

    /// <summary>
    /// Splits the semicolon-separated configuration value into usable prefixes. An entry that is
    /// blank, unparseable, not http(s), hostless, carrying userinfo, or carrying a query/fragment
    /// of its own (it could never be matched, since matching ignores the query) is dropped: a
    /// malformed entry grants nothing, and dropping it keeps every other entry working.
    /// </summary>
    private static IReadOnlyList<AllowedPrefix> Parse(string? configuredPrefixes)
    {
        if (string.IsNullOrWhiteSpace(configuredPrefixes))
        {
            return [];
        }

        var prefixes = new List<AllowedPrefix>();

        foreach (var entry in configuredPrefixes.Split(';', StringSplitOptions.TrimEntries))
        {
            if (entry.Length == 0
                || !Uri.TryCreate(entry, UriKind.Absolute, out var parsed)
                || !IsHttpScheme(parsed.Scheme)
                || parsed.Host.Length == 0
                || parsed.UserInfo.Length > 0
                || parsed.Query.Length > 0
                || parsed.Fragment.Length > 0)
            {
                continue;
            }

            prefixes.Add(new AllowedPrefix(
                parsed.Scheme, parsed.Host, parsed.Port, NormalizePath(parsed.AbsolutePath)));
        }

        return prefixes;
    }

    /// <summary>
    /// The path rule: the candidate path must be the configured path itself or extend it only at a
    /// <c>/</c> segment boundary. A prefix that is not boundary-checked is a suffix-confusion hole
    /// — <c>/signin-handshake-evil</c> and <c>/signin-handshakeX</c> are different resources on the
    /// allowlisted origin, so they must not inherit the callback's trust.
    /// </summary>
    private static bool MatchesPath(string prefixPath, string candidatePath)
        => string.Equals(candidatePath, prefixPath, StringComparison.Ordinal)
            || candidatePath.StartsWith(prefixPath + "/", StringComparison.Ordinal);

    /// <summary>
    /// Strips a configured entry's trailing <c>/</c> so the boundary check can never compare
    /// against a doubled <c>//</c> (<c>/signin-handshake/</c> and <c>/signin-handshake</c> are the
    /// same prefix). A bare <c>/</c> entry is kept as-is, and therefore matches the root path only
    /// — the boundary rule applies to it exactly as to every other entry.
    /// </summary>
    private static string NormalizePath(string path)
        => path.Length > 1 ? path.TrimEnd('/') : path;

    /// <summary>
    /// Whether the candidate's path carries a <c>.</c> or <c>..</c> segment <b>as written</b>.
    /// <para>
    /// Read off the caller's own text (<see cref="Uri.OriginalString"/>) on purpose: by the time a
    /// parsed <see cref="Uri"/> exists it has already applied RFC 3986 <c>remove_dot_segments</c>,
    /// so the spelling is unrecoverable — <c>/signin-handshake/../evil</c> arrives as <c>/evil</c>
    /// (refused for being a different resource), but <c>/signin-handshake/./continuation</c> arrives
    /// as <c>/signin-handshake/continuation</c>, which the boundary check accepts. A downstream
    /// decoder is free to resolve the same raw spelling to a different resource on the allowlisted
    /// origin, so the spelling itself is refused. The portal's copy of this control
    /// (<c>_split_uri</c> in <c>app.py</c>, whose <c>urlsplit</c> preserves the raw path) refuses
    /// exactly the same spellings — the two implementations of this one control must stay
    /// contractually identical, which is why this refusal is explicit rather than inherited from
    /// the parser's normalization.
    /// </para>
    /// </summary>
    private static bool HasDotSegment(string rawUri)
    {
        var separator = rawUri.IndexOf("//", StringComparison.Ordinal);
        var authorityStart = separator < 0 ? 0 : separator + 2;
        var authorityEnd = rawUri.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0 || rawUri[authorityEnd] != '/')
        {
            return false;
        }

        var pathEnd = rawUri.IndexOfAny(['?', '#'], authorityEnd);
        var rawPath = pathEnd < 0 ? rawUri[authorityEnd..] : rawUri[authorityEnd..pathEnd];
        return rawPath.Split('/').Any(segment => segment is "." or "..");
    }

    /// <summary>Only the browser-facing schemes are allowlistable — a configured
    /// <c>ftp://</c>/<c>file://</c> entry is malformed, not an alternative transport.</summary>
    private static bool IsHttpScheme(string scheme)
        => string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);

    /// <summary>One configured prefix, normalized by <see cref="Uri"/>: the port is the scheme's
    /// default when the entry omits it (so <c>http://host/x</c> and <c>http://host:80/x</c> are the
    /// same destination) and the path always starts with <c>/</c>.</summary>
    private readonly record struct AllowedPrefix(string Scheme, string Host, int Port, string Path);
}
