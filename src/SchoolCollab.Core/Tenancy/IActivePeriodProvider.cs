namespace SchoolCollab.Core.Tenancy;

/// <summary>
/// A tenant-scoped projection of the active ("open") period. Defined in Core so
/// other modules (Assignments, …) can resolve the active period without taking a
/// dependency on Students.Core. The implementation lives in Students.Core.
/// See active-period-per-tenancy.md.
/// </summary>
public sealed record ActivePeriod(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status);

/// <summary>
/// Resolves the active (or date-derived "current") period for the current
/// tenant as a layered ambient value — NOT part of <see cref="ITenantProvider"/> /
/// <see cref="TenantContext"/>. Implemented in Students.Core; consumed by any module
/// that needs the tenant's open period (enrollment gating, assignments, …).
/// </summary>
public interface IActivePeriodProvider
{
    /// <summary>The single Active period for the current tenant, or null.</summary>
    Task<ActivePeriod?> GetActivePeriodAsync(CancellationToken ct = default);

    /// <summary>The date-derived "current" period for the current tenant, or null
    /// (display use; see grade-level-setup.md §0.3).</summary>
    Task<ActivePeriod?> GetCurrentPeriodAsync(CancellationToken ct = default);
}
