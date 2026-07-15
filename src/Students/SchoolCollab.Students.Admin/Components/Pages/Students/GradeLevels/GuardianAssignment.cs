using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels;

/// <summary>
/// One guardian to attach to a student in the <see cref="GradeLevelWizard"/>.
/// Either links an existing tenant guardian (<see cref="ExistingGuardianId"/>
/// set) or creates a new one. Resolved into CreateGuardianAsync /
/// LinkGuardianAsync calls by the wizard's SaveAsync Phase 4. Every guardian
/// added through the wizard is Primary — the role is not surfaced in the UI.
/// </summary>
public sealed record GuardianAssignment(
    Guid? ExistingGuardianId,
    string FirstName,
    string LastName,
    Guid? RelationshipCodedValueId,
    ContactChannel? ContactChannel,
    string? ContactValue,
    Guid? TitleCodedValueId,
    GuardianRole Role = GuardianRole.Primary);
