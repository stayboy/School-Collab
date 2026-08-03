using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.GetOrCreateTopic;

/// <summary>
/// Find-or-create by <see cref="GetOrCreateTopic.CodedValueId"/>. Reuses the
/// existing subject (updating mirrored Name/DisplayOrder) or creates a new
/// shared, global one, then links it to the grade via the
/// <see cref="GradeSubjectAssignment"/> bridge — effective from today and
/// open-ended (date-based, not period-bound). Returns a <see cref="TopicDto"/>.
/// Safe under the unique index on <c>CodedValueId</c> (§5.7). Invalidates the
/// <c>students</c> cache tag.
/// </summary>
public sealed class GetOrCreateTopicHandler(
    ITopicRepository repository,
    IGradeSubjectAssignmentRepository assignmentRepository,
    IGradeLevelRepository gradeLevelRepository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<GetOrCreateTopicHandler> logger) : ICommandHandler<GetOrCreateTopic, TopicDto>
{
    public async Task<TopicDto> HandleAsync(
        GetOrCreateTopic command,
        CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(GetOrCreateTopic), typeof(Topic));

        logger.LogDebug("Handling GetOrCreateTopic for CodedValueId {Id}", command.CodedValueId);

        // 1. Verify the grade level exists.
        _ = await gradeLevelRepository.GetAsync(command.GradeLevelId, cancellationToken)
            ?? throw new GradeLevelNotFoundException(command.GradeLevelId);

        // 2. The bridge is date-based: a new assignment opens today, open-ended.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var existing = await repository.GetByCodedValueIdAsync(command.CodedValueId, cancellationToken);

        Topic subject;
        bool created;

        if (existing is not null)
        {
            existing.Update(command.Name, command.DisplayOrder);
            await repository.UpdateAsync(existing, cancellationToken);
            subject = existing;
            created = false;
            logger.LogInformation("Topic {Id} reused for CodedValueId {CodedValueId} (mirrored fields updated)",
                subject.Id, command.CodedValueId);
        }
        else
        {
            subject = Topic.Create(
                    command.CodedValueId,
                    command.Code,
                    command.Name,
                    command.DisplayOrder)
                .WithTenant(tenantProvider);
            await repository.AddAsync(subject, cancellationToken);
            created = true;
            logger.LogInformation("Topic {Id} created for CodedValueId {CodedValueId}",
                subject.Id, command.CodedValueId);
        }

        // 3. Link the shared topic to the grade via the bridge, effective today.
        var existingAssignments = await assignmentRepository
            .ListByGradeLevelAsync(command.GradeLevelId, today, cancellationToken);

        if (!existingAssignments.Any(a => a.TopicId == subject.Id))
        {
            var assignment = GradeSubjectAssignment.Create(
                    command.GradeLevelId,
                    activityGroupId: null,
                    subject.Id,
                    today)
                .WithTenant(tenantProvider);

            await assignmentRepository.AddAsync(assignment, cancellationToken);
            assignment.ClearDomainEvents();
            logger.LogInformation(
                "GradeSubjectAssignment created for grade {GradeLevelId}, topic {TopicId}, from {StartDate}",
                command.GradeLevelId, subject.Id, today);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);
        subject.ClearDomainEvents();

        return new TopicDto(
            subject.Id,
            subject.CodedValueId,
            subject.Code,
            subject.Name,
            subject.Description,
            subject.DisplayOrder,
            subject.CreatedAt,
            subject.UpdatedAt);
    }
}
