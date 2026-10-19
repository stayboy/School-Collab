using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ResolveShortLink;

/// <summary>
/// Resolves a public short-link code to the contact-scoped deep-link token it stands for
/// (round notification-templates-b2a, spec §3.6). Read by the Families host's public
/// <c>/l/{code}</c> route through a tenant-less cross-module client. Null (404) for an unknown or
/// expired code.
/// </summary>
public sealed record ResolveShortLink(string Code) : IQuery<ShortLinkResolutionDto?>;

/// <summary>
/// Handles <see cref="ResolveShortLink"/>.
///
/// <para><b>Deliberately cross-tenant and anonymous.</b> The code IS the credential: the caller is
/// an unauthenticated visitor arriving from an SMS, so there is no tenant to scope the read by and
/// no principal to check. The bypass is kept to <b>one indexed single-row query</b> —
/// <c>IgnoreQueryFilters(["Tenant"])</c> over the global unique index on
/// <c>ShortLinkCode</c> (the <c>NotificationDispatchService</c> sanctioned-read posture); a broader
/// bypass would be a leak, and the global tenant filter alone would return nothing without a
/// tenant context. Nothing but the token leaves this handler: no ids, no names, no expiry.</para>
/// </summary>
public sealed class ResolveShortLinkHandler(
    AssignmentsDbContext db,
    TimeProvider timeProvider,
    ILogger<ResolveShortLinkHandler> logger)
    : IQueryHandler<ResolveShortLink, ShortLinkResolutionDto?>
{
    /// <inheritdoc />
    public async Task<ShortLinkResolutionDto?> HandleAsync(
        ResolveShortLink query, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        // Unknown code and expired code are the SAME outcome (404): the landing falls through to the
        // friendly expired page either way, so the response never distinguishes "never existed" from
        // "used to work" — a distinction only an attacker would enjoy.
        var token = await db.AssignmentRecipients
            .IgnoreQueryFilters(["Tenant"])
            .AsNoTracking()
            .Where(x => x.ShortLinkCode == query.Code
                && x.DeepLinkToken != null
                && x.DeepLinkExpiresAt != null
                && x.DeepLinkExpiresAt > now)
            .Select(x => x.DeepLinkToken)
            .FirstOrDefaultAsync(cancellationToken);

        if (token is null)
        {
            logger.LogInformation("Short link did not resolve to an unexpired recipient link");
            return null;
        }

        return new ShortLinkResolutionDto(token);
    }
}
