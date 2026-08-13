using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.CreateTeacherWithAssignments;

/// <summary>
/// Atomically creates a teacher with grade and activity assignments (Unit of Work pattern).
/// All operations succeed or fail together — if any assignment link fails, the entire
/// transaction is rolled back.
/// </summary>
public sealed record CreateTeacherWithAssignments(
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    Guid? GenderCodedValueId = null,
    DateOnly? DateOfBirth = null,
    Guid? LevelOfEducationCodedValueId = null,
    Guid[]? QualificationCodedValueIds = null,
    GradeAssignment[]? GradeAssignments = null,
    ActivityAssignment[]? ActivityAssignments = null) : ICommand;

/// <summary>A grade assignment row: grade + optional subject + optional role.</summary>
public sealed record GradeAssignment(
    Guid GradeLevelId,
    Guid? SubjectId = null,
    Guid? RoleCodedValueId = null);

/// <summary>An activity assignment row: activity + optional role + optional grades.</summary>
public sealed record ActivityAssignment(
    Guid ActivityGroupId,
    Guid? RoleCodedValueId = null,
    Guid[]? GradeLevelIds = null);
