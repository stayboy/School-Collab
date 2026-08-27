using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.UpdateActivityGroup;

public sealed record UpdateActivityGroup(
    Guid Id,
    string Name,
    string? Description = null,
    string? Category = null,
    int? Capacity = null,
    DateOnly? EnrollmentStartDate = null,
    DateOnly? EnrollmentEndDate = null,
    bool? AutoRenewDefault = null,
    Guid[]? EligibleGradeIds = null) : ICommand;