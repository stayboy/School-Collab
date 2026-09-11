using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Commands.UpsertTenantAssignmentPolicy;

/// <summary>
/// Creates or replaces the current tenant's default guardian-signature policy
/// (WS-C1 / spec §7 Q1). The returned
/// <see cref="TenantAssignmentPolicyDto"/> is the persisted policy.
/// </summary>
public sealed record UpsertTenantAssignmentPolicy(bool RequiresSignatureDefault) : ICommand;
