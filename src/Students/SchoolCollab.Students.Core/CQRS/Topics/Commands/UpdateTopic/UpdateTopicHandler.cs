using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.UpdateTopic;

public sealed class UpdateTopicHandler(
    ITopicRepository repository,
    HybridCache cache,
    ILogger<UpdateTopicHandler> logger) : ICommandHandler<UpdateTopic>
{
    public async Task HandleAsync(UpdateTopic command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling UpdateTopic {Id}", command.Id);

        var subject = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new TopicNotFoundException(command.Id);

        subject.Update(command.Name, command.DisplayOrder, codedValueId: command.CodedValueId, code: command.Code);

        try
        {
            await repository.UpdateAsync(subject, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Topic", subject.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        subject.ClearDomainEvents();

        logger.LogInformation("Topic {Id} updated", subject.Id);
    }
}