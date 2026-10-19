namespace SchoolCollab.Families.DeepLinks;

/// <summary>
/// Round notification-templates-b2a (spec §3.6, Q61) — the repo's second public, no-auth route
/// group: the SMS/WhatsApp short link. Mapped outside <c>MapRazorComponents</c> (which is
/// <c>RequireAuthorization</c>'d when OIDC is on) and with no authorization at all — the code in
/// the URL IS the credential, exactly as <see cref="DeepLinkEndpoints"/>' bearer token is.
///
/// <para>The route answers a <b>302</b>: a live code to the existing <c>/deeplink/{token}</c>
/// landing (which validates the token, signs the guardian in and stamps <c>OpenedAt</c>), an
/// unknown/expired/malformed one to the friendly expired page.</para>
/// </summary>
public static class ShortLinkRoutes
{
    /// <summary>The route group prefix of the public short link (shared with the Assignments side
    /// that composes <c>{base}/l/{code}</c> — <c>DeepLinkConstants.ShortLinkPathPrefix</c>).</summary>
    public const string GroupPrefix = "/l";

    /// <summary>Maps the public <c>/l/{code}</c> landing route.</summary>
    public static RouteGroupBuilder MapShortLinkRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/{code}", async (
            string code,
            ShortLinkLandingService landing,
            CancellationToken ct) => Results.Redirect(await landing.ResolveRedirectAsync(code, ct)));

        return group;
    }
}
