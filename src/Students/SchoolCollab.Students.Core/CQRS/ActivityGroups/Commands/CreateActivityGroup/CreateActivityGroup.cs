using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.CreateActivityGroup;

public sealed record CreateActivityGroup(
    string Name,
    string? Description = null,
    string? Category = null,
    int? Capacity = null,
    EnrollmentSpan Span = EnrollmentSpan.OpenEnded,
    DateOnly? EnrollmentStartDate = null,
    DateOnly? EnrollmentEndDate = null,
    bool AutoRenewDefault = true,
    Guid[]? EligibleGradeIds = null) : ICommand;