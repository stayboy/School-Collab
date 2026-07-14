using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels;

/// <summary>
/// One drafted guardian awaiting creation on save. Captured by
/// <see cref="GuardianDraftList"/> as the user fills in the per-student
/// "add guardian" form. Read by the parent wizard's SaveAsync Phase 4,
/// which creates the guardian, links them to the student, and (optionally)
/// seeds a contact from the captured channel + value.
/// </summary>
public sealed record GuardianDraft(
    string FirstName,
    string LastName,
    Guid? RelationshipCodedValueId,
    GuardianRole Role,
    ContactChannel? ContactChannel,
    string? ContactValue,
    Guid? TitleCodedValueId);
