using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeStreams.Commands.RemoveGradeStream;

/// <summary>Stops offering a stream for a grade level (bridge-row delete; the coded value stays).</summary>
public sealed record RemoveGradeStream(Guid GradeLevelId, Guid AssignmentId) : ICommand;
