using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Admin.Shared.Components;

namespace SchoolCollab.Students.Admin.Components.Students;

/// <summary>
/// One guardian to attach to a student. Used by the student Detail/Edit
/// pages when adding guardians via the <see cref="GuardianPickerDialog"/>
/// or <see cref="GuardianFormDialog"/>. Either links an existing tenant
/// guardian (<see cref="ExistingGuardianId"/> set) or creates a new one.
/// </summary>
public sealed record GuardianAssignment(
    Guid? ExistingGuardianId,
    string FirstName,
    string LastName,
    Guid? RelationshipCodedValueId,
    ContactChannel? ContactChannel,
    string? ContactValue,
    Guid? TitleCodedValueId,
    string? CountryCode = null,
    GuardianRole Role = GuardianRole.Primary,
    IReadOnlyList<ContactModel>? Contacts = null);