using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeStreams.Commands.AssignGradeStream;

/// <summary>Offers an existing stream coded value for a grade level (idempotent bridge insert).</summary>
public sealed record AssignGradeStream(Guid GradeLevelId, Guid StreamCodedValueId) : ICommand;
