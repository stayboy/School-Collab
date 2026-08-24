using Microsoft.Extensions.Caching.Hybrid;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.SetCodedValueAttribute;

public sealed class SetCodedValueAttributeHandler(
    ICodedValueRepository repository,
    IIntegrationEventPublisher publisher,
    HybridCache cache) : ICommandHandler<SetCodedValueAttribute>
{
    public async Task HandleAsync(SetCodedValueAttribute command, CancellationToken cancellationToken = default)
    {
        var codedValue = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new CodedValueNotFoundException(command.Id);

        // Stream uniqueness validation (FR-9): when setting a streamVersion on a child
        // of GRSTREAMS, the (gradeLevel, streamVersion) pair must be unique per grade.
        // The prefix rule is RELAXED — we do NOT validate that the streamVersion's
        // leading digits match the grade's DisplayOrder.
        if (command.Key == "streamVersion" && codedValue.ParentId is not null)
        {
            var parent = await repository.GetAsync(codedValue.ParentId.Value, cancellationToken);
            if (parent is not null && parent.Code == "GRSTREAMS")
            {
                var gradeLevelAttribute = codedValue.Attributes
                    .FirstOrDefault(a => a.Key == "gradeLevel");
                if (gradeLevelAttribute is not null)
                {
                    var duplicateStream = await repository.FindStreamSiblingAsync(
                        codedValue.ParentId.Value,
                        gradeLevelAttribute.Value,
                        command.Value,
                        cancellationToken);
                    if (duplicateStream is not null && duplicateStream.Id != codedValue.Id)
                    {
                        throw new DuplicateStreamException(
                            gradeLevelAttribute.Value,
                            command.Value,
                            duplicateStream.Id);
                    }
                }
            }
        }

        codedValue.SetAttribute(command.Key, command.Value);
        await repository.UpdateAsync(codedValue, cancellationToken);
        await cache.RemoveByTagAsync("coded-values", cancellationToken);

        // Attribute values drive downstream validation (e.g. a stream's gradeLevel
        // attribute is read by Students enroll stream validation), so attribute
        // changes MUST reach the projection — publish the full current state
        // (adr-cross-module-calls.md Phase 0 gap fix).
        var parentCode = await CodedValueEventMapper.ResolveParentCodeAsync(
            repository, codedValue.ParentId, cancellationToken);
        await publisher.EnqueueAsync(codedValue.ToUpdatedEvent(parentCode), cancellationToken);
    }
}