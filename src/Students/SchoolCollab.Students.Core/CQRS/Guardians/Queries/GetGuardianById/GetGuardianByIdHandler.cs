using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Guardians.Queries.GetGuardianById;

public sealed class GetGuardianByIdHandler(
    IGuardianRepository repository) : IQueryHandler<GetGuardianById, GuardianDto?>
{
    public async Task<GuardianDto?> HandleAsync(GetGuardianById query, CancellationToken cancellationToken = default)
    {
        var g = await repository.GetAsync(query.Id, cancellationToken);
        return g is null
            ? null
            : new GuardianDto(
                g.Id, g.TitleCodedValueId, g.FirstName, g.LastName, g.DisplayName, g.Address, g.CommunityId,
                g.IsDeleted, g.CreatedAt, g.UpdatedAt);
    }
}
