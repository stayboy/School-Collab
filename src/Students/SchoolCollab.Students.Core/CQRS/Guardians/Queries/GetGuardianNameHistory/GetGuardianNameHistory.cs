using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.GetGuardianNameHistory;

public sealed record GetGuardianNameHistory(Guid GuardianId) : IQuery<GuardianNameHistoryDto[]>;
