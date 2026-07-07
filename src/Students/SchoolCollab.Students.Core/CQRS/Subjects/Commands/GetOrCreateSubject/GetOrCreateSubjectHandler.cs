using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.GetOrCreateSubject;

/// <summary>
/// Find-or-create by <see cref="GetOrCreateSubject.CodedValueId"/>. Reuses the
/// existing subject (updating mirrored Name/DisplayOrder) or creates a new one,
/// then returns a <see cref="SubjectDto"/>. Safe under the unique index on
/// <c>CodedValueId</c> (§5.7). Invalidates the <c>students</c> cache tag.
/// </summary>
public sealed class GetOrCreateSubjectHandler(
    ISubjectRepository repository,
    HybridCache cache,
    ILogger<GetOrCreateSubjectHandler> logger) : ICommandHandler<GetOrCreateSubject, SubjectDto>
{
    public async Task<SubjectDto> HandleAsync(
        GetOrCreateSubject command,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling GetOrCreateSubject for CodedValueId {Id}", command.CodedValueId);

        var existing = await repository.GetByCodedValueIdAsync(command.CodedValueId, cancellationToken);

        Subject subject;
        bool created;

        if (existing is not null)
        {
            existing.Update(command.Name, command.DisplayOrder);
            await repository.UpdateAsync(existing, cancellationToken);
            subject = existing;
            created = false;
            logger.LogInformation("Subject {Id} reused for CodedValueId {CodedValueId} (mirrored fields updated)",
                subject.Id, command.CodedValueId);
        }
        else
        {
            subject = Subject.Create(command.CodedValueId, command.Code, command.Name, command.DisplayOrder);
            await repository.AddAsync(subject, cancellationToken);
            created = true;
            logger.LogInformation("Subject {Id} created for CodedValueId {CodedValueId}",
                subject.Id, command.CodedValueId);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);
        subject.ClearDomainEvents();

        return new SubjectDto(
            subject.Id,
            subject.CodedValueId,
            subject.Code,
            subject.Name,
            subject.DisplayOrder,
            subject.CreatedAt,
            subject.UpdatedAt);
    }
}
