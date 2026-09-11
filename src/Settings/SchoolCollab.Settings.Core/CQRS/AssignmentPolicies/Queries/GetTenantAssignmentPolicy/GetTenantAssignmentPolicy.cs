using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Queries.GetTenantAssignmentPolicy;

/// <summary>
/// Returns the current tenant's default guardian-signature policy, or
/// <see langword="null"/> if none has been configured yet. A null result means the
/// caller falls back to <see langword="false"/> (WS-C1 / spec §7 Q1).
/// </summary>
public sealed record GetTenantAssignmentPolicy : IQuery<TenantAssignmentPolicyDto?>
{
    public static readonly GetTenantAssignmentPolicy Instance = new();
}
