namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// Wire shape for a <see cref="Domain.TenantAssignmentPolicy" /> (the global
/// guardian-signature default for a tenant, WS-C1 / spec §7 Q1). A 204/absent
/// result means no row exists yet — callers fall back to
/// <see langword="false"/>.
/// </summary>
public sealed record TenantAssignmentPolicyDto(bool RequiresSignatureDefault);
