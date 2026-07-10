using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Tenancy;

/// <summary>
/// Students.Core implementation of <see cref="IActivePeriodProvider"/>. Reads the
/// current tenant via the repository's tenant-filtered query and resolves the
/// active/current period. Registered scoped (see Extensions.AddStudentsCore) so it
/// is per-request and respects <c>RunWithExplicitTenantAsync</c> for workers.
/// </summary>
/// <remarks>
/// The active period is resolved directly from the tenant-filtered repository. A
/// per-tenant cache (HybridCache tag "students", already invalidated by the
/// Activate/Complete handlers) can layer on top later if needed.
/// </remarks>
public sealed class ActivePeriodProvider(IPeriodRepository periodRepository) : IActivePeriodProvider
{
    public async Task<ActivePeriod?> GetActivePeriodAsync(CancellationToken ct = default)
    {
        var period = await periodRepository.GetActivePeriodAsync(cancellationToken: ct);
        return period is null ? null : ToActivePeriod(period);
    }

    public async Task<ActivePeriod?> GetCurrentPeriodAsync(CancellationToken ct = default)
    {
        var period = await periodRepository.GetCurrentPeriodAsync(ct);
        return period is null ? null : ToActivePeriod(period);
    }

    private static ActivePeriod ToActivePeriod(Period p) =>
        new(p.Id, p.Name, p.StartDate, p.EndDate, p.Status.ToString());
}
