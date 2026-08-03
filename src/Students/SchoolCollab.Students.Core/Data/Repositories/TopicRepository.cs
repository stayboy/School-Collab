using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class TopicRepository(StudentsDbContext db)
    : RepositoryBase<Topic, StudentsDbContext>(db), ITopicRepository
{
    public Task<Topic?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Db.Topics.FirstOrDefaultAsync(x => x.Code == normalized, cancellationToken);
    }

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Db.Topics.AnyAsync(x => x.Code == normalized, cancellationToken);
    }

    public Task<Topic?> GetByCodedValueIdAsync(Guid codedValueId, CancellationToken cancellationToken = default) =>
        Db.Topics.FirstOrDefaultAsync(x => x.CodedValueId == codedValueId, cancellationToken);

    public override async Task UpdateAsync(Topic subject, CancellationToken cancellationToken = default)
    {
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(subject.Id);
        }
    }

    public async Task<TopicDto[]> ListAsync(CancellationToken cancellationToken = default) =>
        await Db.Topics
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new TopicDto(
                x.Id, x.CodedValueId, x.Code, x.Name,
                x.Description, x.DisplayOrder,
                x.CreatedAt, x.UpdatedAt))
            .ToArrayAsync(cancellationToken);
}
