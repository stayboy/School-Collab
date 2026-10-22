using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.Services;

/// <summary>
/// The tenant-default invariant (round notification-templates-b2c, spec Q15/Q66): a template with an
/// <b>empty constraint set</b> is the tenant default for its (kind, channel), so at most one such row
/// may be live. Create and update both run this check inside their own transaction (one
/// <see cref="SettingsDbContext.SaveChangesAsync"/> per mutation), so a rejected write leaves no row
/// and no audit entry behind.
///
/// <para><b>Accepted race (recorded, not serialised).</b> Two concurrent creates can both pass the
/// pre-insert check and leave two unconstrained rows: the unique index that used to make that
/// impossible is relaxed to non-unique in this round so candidates can coexist. The window is
/// accepted because this is an admin-only surface with a single-writer UI, and serialising it (a
/// table lock or a post-insert re-check) would cost every template write to close a race the product
/// cannot currently reach. A post-insert re-check in the same transaction is the fallback if it ever
/// bites.</para>
/// </summary>
public static class TenantNotificationTemplateDefaults
{
    /// <summary>
    /// Throws <see cref="TenantNotificationTemplateConflictException"/> when another live
    /// unconstrained template exists for <paramref name="kind"/> × <paramref name="channel"/> <b>in the
    /// same scope</b> (<paramref name="ownerId"/>: a tenant id, or <see langword="null"/> for the system
    /// blueprint — redesign spec §3.7, so one unconstrained tenant default and one unconstrained system
    /// default may coexist). <paramref name="excludingTemplateId"/> is the row being updated (null on
    /// create) so a template can be updated in place without colliding with itself.
    /// </summary>
    public static async Task EnsureNoCompetingDefaultAsync(
        SettingsDbContext db,
        NotificationTemplateKind kind,
        NotificationChannel channel,
        Guid? ownerId,
        Guid? excludingTemplateId,
        CancellationToken cancellationToken = default)
    {
        var query = db.TenantNotificationTemplates
            .AsNoTracking()
            .Where(x => x.Kind == kind && x.Channel == channel && x.TenantId == ownerId
                && !x.Constraints.Any());

        if (excludingTemplateId is { } excluded)
        {
            query = query.Where(x => x.Id != excluded);
        }

        var competing = await query.Select(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (competing != Guid.Empty)
        {
            var scope = ownerId is null ? "the system blueprint" : "this tenant";
            throw new TenantNotificationTemplateConflictException(
                $"A default {kind}/{channel} template already exists for {scope}. A template with no "
                + "constraints IS the default, so exactly one may exist per scope — add a constraint "
                + "dimension to this one, or edit the existing default instead.");
        }
    }
}
