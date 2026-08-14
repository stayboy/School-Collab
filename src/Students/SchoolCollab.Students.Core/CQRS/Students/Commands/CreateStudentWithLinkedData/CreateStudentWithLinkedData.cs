using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Students.Commands.CreateStudentWithLinkedData;

/// <summary>
/// Atomically creates a student with its linked guardians, optional contacts, and an
/// optional enrollment (Unit of Work pattern). All operations succeed or fail together —
/// no orphaned student, no partial guardian set, no "student exists but not on the grade
/// card" state. Mirrors <c>CreateTeacherWithAssignments</c> for the student graph.
/// </summary>
public sealed record CreateStudentWithLinkedData(
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    Guid? TitleCodedValueId = null,
    GuardianDraft[]? Guardians = null,
    Guid? EnrollmentGradeLevelId = null,
    Guid? EnrollmentPeriodId = null,
    Guid? StreamCodedValueId = null,
    DateOnly? EnrolledOn = null,
    ContactDraft[]? Contacts = null) : ICommand;

/// <summary>
/// A guardian to link to the newly created student. Either references an existing
/// guardian (<see cref="ExistingGuardianId"/>) or supplies new-guardian demographics
/// (the remaining fields) to create one atomically within the same unit of work.
/// </summary>
public sealed record GuardianDraft(
    Guid? ExistingGuardianId,
    GuardianRole Role,
    Guid? RelationshipCodedValueId = null,
    bool IsEmergencyContact = false,
    Guid? ActingGuardianId = null,
    Guid? TitleCodedValueId = null,
    string? FirstName = null,
    string? LastName = null,
    Guid? GenderCodedValueId = null,
    DateOnly? DateOfBirth = null);

/// <summary>A contact to attach to the newly created student (reserved shape).
/// <c>Id</c> is null for a new contact; set for an update (the all-inclusive edit
/// reconciles contacts by id).</summary>
public sealed record ContactDraft(
    ContactChannel Channel,
    string Value,
    string? Label = null,
    string? CountryCode = null,
    int DisplayOrder = 0,
    Guid? Id = null);
