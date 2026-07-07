using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.GetOrCreateSubject;

/// <summary>
/// Find-or-create a <see cref="Domain.Subject"/> by <see cref="CodedValueId"/>
/// (safe since the unique index from §5.7 guarantees at most one subject per
/// coded value). If a subject for the coded value exists, it is reused and its
/// mirrored <see cref="Domain.Subject.Name"/>/DisplayOrder are updated;
/// otherwise a new one is created. Used by the wizard's "Add to grade" button
/// so the user can pick a subject coded value and wire it to the grade without
/// leaving the wizard. Returns the resulting subject as a <see cref="SubjectDto"/>.
/// </summary>
public sealed record GetOrCreateSubject(
    Guid CodedValueId,
    string Code,
    string Name,
    int DisplayOrder) : ICommand;
