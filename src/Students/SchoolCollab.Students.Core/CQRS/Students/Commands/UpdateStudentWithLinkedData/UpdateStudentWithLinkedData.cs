using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.CQRS.Students.Commands.CreateStudentWithLinkedData;

namespace SchoolCollab.Students.Core.CQRS.Students.Commands.UpdateStudentWithLinkedData;

/// <summary>
/// Atomically updates a student's profile and reconciles its guardians and contacts in
/// one unit of work — the edit counterpart of <c>CreateStudentWithLinkedData</c>. All
/// operations succeed or fail together: no profile saved with a half-applied guardian
/// set, no orphaned link, no "student updated but contacts stale" state.
///
/// Optimistic concurrency: <see cref="ExpectedRowVersion"/> is the student's Postgres
/// <c>xmin</c> row version the client loaded; the handler rejects a stale save with
/// <c>ConcurrencyException</c>. <see cref="LoadedGuardianIds"/> / <see cref="LoadedContactIds"/>
/// are the guardian-link guardian-ids / contact-ids the client saw at load, so the handler
/// can also detect a guardian/contact added by another user since the client loaded.
/// </summary>
public sealed record UpdateStudentWithLinkedData(
    Guid Id,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    Guid? TitleCodedValueId = null,
    uint ExpectedRowVersion = 0,
    GuardianDraft[]? Guardians = null,
    ContactDraft[]? Contacts = null,
    Guid[]? LoadedGuardianIds = null,
    Guid[]? LoadedContactIds = null) : ICommand;
