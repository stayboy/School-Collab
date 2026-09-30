using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeStreams.Queries.ListGradeStreams;

/// <summary>Lists the streams offered by a grade level (bridge rows + resolved coded-value metadata).</summary>
public sealed record ListGradeStreams(Guid GradeLevelId) : IQuery<GradeStreamDto[]>;
