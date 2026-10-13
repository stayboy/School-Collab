using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// The client-side <see cref="AuthenticationStateProvider"/> the Blazor Hybrid head supplies
/// (spec D6). A <c>BlazorWebView</c> brings none of its own, and the shared gates
/// (<c>GateBase</c>/<c>TenantGate</c>) inject this service and re-evaluate whenever
/// <c>AuthenticationStateChanged</c> fires — so without it the design system cannot gate anything
/// at all.
/// </summary>
/// <remarks>
/// <para>Claim spellings are the <b>raw realm spellings</b> (<c>tenant_id</c>, <c>tenant_name</c>,
/// <c>roles</c>) because the shared UI reads them verbatim
/// (<c>VisibleTenantService</c>'s <c>user.FindFirst("tenant_id")</c>). Nothing is renamed to
/// <c>ClaimTypes.*</c> — that mapping is the server's, and the server's principal is not here.</para>
/// <para>The component subscribes to <see cref="TokenSession.SessionChanged"/> only: the session
/// signals, this provider notifies the gates. It never subscribes to its own
/// <c>AuthenticationStateChanged</c>, so there is exactly one direction and no loop.</para>
/// <para>Nothing on this path logs, and no token material reaches an exception message.</para>
/// </remarks>
public sealed class MobileAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    /// <summary>The scheme name on the principal produced from a stored session.</summary>
    private const string AuthenticationType = "Bearer";

    private readonly TokenSession _session;

    /// <param name="session">The token session whose claims back the auth state.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public MobileAuthenticationStateProvider(TokenSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _session.SessionChanged += OnSessionChanged;
    }

    /// <inheritdoc />
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // Reads the persisted session on first use, and does nothing on every later call.
        await _session.EnsureInitializedAsync();

        var token = _session.AccessToken;
        if (string.IsNullOrEmpty(token))
        {
            return CreateAnonymousState();
        }

        try
        {
            var claims = JwtPayloadReader.ReadClaimValues(token)
                .SelectMany(claim => claim.Value.Select(value => new Claim(claim.Key, value)));

            return new AuthenticationState(
                new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType)));
        }
        catch (FormatException)
        {
            // A token we cannot read cannot authenticate anybody: show the signed-out state rather
            // than a half-built principal. The APIs would refuse the same token anyway.
            return CreateAnonymousState();
        }
    }

    /// <summary>
    /// A signed-out state, minted per call. It is deliberately NOT a cached static: unrelated
    /// consumers must never be handed the same <see cref="ClaimsPrincipal"/> instance, whose
    /// identity collection one of them could then observe as shared state.
    /// </summary>
    private static AuthenticationState CreateAnonymousState() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    /// <summary>Detaches from the session's signal. Host teardown calls this; a live single-app
    /// host never does.</summary>
    public void Dispose() => _session.SessionChanged -= OnSessionChanged;

    private void OnSessionChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
