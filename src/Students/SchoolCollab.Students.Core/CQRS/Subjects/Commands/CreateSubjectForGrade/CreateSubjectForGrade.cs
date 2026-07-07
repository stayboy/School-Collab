using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubjectForGrade;

/// <summary>
/// Creates a <see cref="Domain.Subject"/> (find-or-create by
/// <paramref name="CodedValueId"/> if provided, else by <paramref name="Code"/>)
/// **and** a <see cref="Domain.GradeSubjectAssignment"/> linking it to the given
/// grade level for the <b>current period</b> (derived server-side). Used by the
/// Subjects landing page's <c>+ New Subject</c> tool (§8.1). Returns the
/// resulting <see cref="DTOs.SubjectDto"/>.
/// </summary>
public sealed record CreateSubjectForGrade(
    Guid GradeLevelId,
    Guid? CodedValueId,
    string Code,
    string Name,
    int DisplayOrder) : ICommand;