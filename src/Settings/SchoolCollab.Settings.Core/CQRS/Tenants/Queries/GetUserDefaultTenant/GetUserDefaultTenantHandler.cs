using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.Tenants.Queries.GetUserDefaultTenant;

/// <summary>
/// Reads the operator's preference row. A missing row is not an error — it is the
/// documented "no preference recorded" state, reported as a null
/// <see cref="UserTenantPreferenceDto.DefaultTenantId"/> rather than a 404, so the portal
/// has one shape to handle.
/// </summary>
public sealed class GetUserDefaultTenantHandler(SettingsDbContext db)
    : IQueryHandler<GetUserDefaultTenant, UserTenantPreferenceDto>
{
    public async Task<UserTenantPreferenceDto> HandleAsync(
        GetUserDefaultTenant query,
        CancellationToken cancellationToken = default)
    {
        var subjectId = query.SubjectId?.Trim() ?? string.Empty;
        if (subjectId.Length == 0)
        {
            // Unauthenticated / claim-less caller: no preference exists to report.
            return new UserTenantPreferenceDto(null);
        }

        var preference = await db.AdminUserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.SubjectId == subjectId, cancellationToken);

        return new UserTenantPreferenceDto(preference?.DefaultTenantId);
    }
}
