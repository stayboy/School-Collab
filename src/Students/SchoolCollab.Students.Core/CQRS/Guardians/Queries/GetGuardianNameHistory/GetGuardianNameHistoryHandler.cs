using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.GetGuardianNameHistory;

public sealed class GetGuardianNameHistoryHandler(
    IGuardianRepository repository) : IQueryHandler<GetGuardianNameHistory, GuardianNameHistoryDto[]>
{
    public async Task<GuardianNameHistoryDto[]> HandleAsync(GetGuardianNameHistory query, CancellationToken cancellationToken = default)
    {
        var history = await repository.GetNameHistoryAsync(query.GuardianId, cancellationToken);
        return history.Select(h => new GuardianNameHistoryDto(
            h.Id, h.GuardianId, h.FirstName, h.LastName, h.DisplayName, h.CreatedAt)).ToArray();
    }
}
