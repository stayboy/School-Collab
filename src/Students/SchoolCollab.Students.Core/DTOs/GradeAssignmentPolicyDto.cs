namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// Wire shape for a <see cref="Domain.GradeAssignmentPolicy"/> (the raw per-grade
/// guardian-signature override). A null <see cref="RequiresSignatureDefault"/>
/// means "inherit the tenant default" (nothing stored for this grade). The
/// effective value is computed by the Assignments resolver fed with the tenant
/// default + this override (WS-C1 / spec §7 Q1).
/// </summary>
public sealed record GradeAssignmentPolicyDto(
    Guid GradeLevelId,
    bool? RequiresSignatureDefault,
    DateTimeOffset UpdatedAt);
