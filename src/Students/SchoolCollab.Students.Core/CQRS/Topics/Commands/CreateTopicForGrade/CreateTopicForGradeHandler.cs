using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicForGrade;

/// <summary>
/// Creates (or reuses) a shared, global <see cref="Topic"/> and links it to the
/// grade level via the <see cref="GradeTopicAssignment"/> bridge (§8.1). The
/// topic itself is a shared catalog definition; the per-grade wiring lives on the
/// bridge. Assignments are <b>date-based, not period-bound</b>: the bridge row is
/// opened today (<see cref="DateOnly"/>) and left open-ended (<c>EndDate = null</c>)
/// so the topic stays assigned across multiple years unless blocked/archived.
/// </summary>
public sealed class CreateTopicForGradeHandler(
    ITopicRepository topicRepository,
    IGradeTopicAssignmentRepository assignmentRepository,
    IGradeLevelRepository gradeLevelRepository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    IEntityCodeGenerator entityCodeGenerator,
    ILogger<CreateTopicForGradeHandler> logger) : ICommandHandler<CreateTopicForGrade, TopicDto>
{
    public async Task<TopicDto> HandleAsync(
        CreateTopicForGrade command,
        CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreateTopicForGrade), typeof(Topic));

        logger.LogDebug(
            "Handling CreateTopicForGrade for grade {GradeLevelId}, code {Code}",
            command.GradeLevelId, command.Code);

        // 1. Verify the grade level exists.
        var gradeLevel = await gradeLevelRepository.GetAsync(command.GradeLevelId, cancellationToken)
            ?? throw new GradeLevelNotFoundException(command.GradeLevelId);

        // 2. The bridge is date-based, not period-bound. A new assignment opens
        //    today and stays open-ended (EndDate = null), so no current period is
        //    required to assign a topic to a grade.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 3. Find-or-create the shared, global Topic.
        //    - If a CodedValueId is provided, look up by it first (the operational
        //      peer of GradeLevel — stable reporting key, §3.2).
        //    - Otherwise fall back to lookup by Code.
        //    - If neither finds an existing Topic, create a new shared one.
        Topic subject = null!;
        bool subjectCreated = false;
        string? code = null;

        if (command.CodedValueId.HasValue)
        {
            subject = await topicRepository.GetByCodedValueIdAsync(command.CodedValueId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(command.Code))
        {
            subject = await topicRepository.GetByCodeAsync(command.Code.Trim(), cancellationToken);
        }

        if (subject is not null)
        {
            // Reuse the existing subject — update mirrored Name/DisplayOrder.
            subject.Update(command.Name, command.DisplayOrder);
            await topicRepository.UpdateAsync(subject, cancellationToken);
            logger.LogInformation("Topic {Id} reused for grade {GradeLevelId}", subject.Id, command.GradeLevelId);
        }
        else
        {
            // tcv/5: when no explicit code is supplied, generate one from the topic
            // name via the seeded TOPIC_CODE rule (WordInitials + NumericSequence),
            // e.g. "computer science" → CS01.
            code = !string.IsNullOrWhiteSpace(command.Code)
                ? command.Code.Trim()
                : await entityCodeGenerator.GenerateWithNameAsync("TOPIC_CODE", command.Name, cancellationToken);

            // Verify the code is not already taken (only relevant when we looked
            // up by CodedValueId and didn't find it but the code is in use).
            if (await topicRepository.ExistsByCodeAsync(code, cancellationToken))
                throw new DuplicateTopicCodeException(code);

            var codedValueId = command.CodedValueId ?? Guid.NewGuid();
            subject = Topic.Create(
                    codedValueId: codedValueId,
                    code: code,
                    name: command.Name,
                    displayOrder: command.DisplayOrder)
                .WithTenant(tenantProvider);
            await topicRepository.AddAsync(subject, cancellationToken);
            subjectCreated = true;
            logger.LogInformation("Topic {Id} created for grade {GradeLevelId}", subject.Id, command.GradeLevelId);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);
        subject.ClearDomainEvents();

        // 4. Retain GradeTopicAssignment as the M:N bridge between the topic and
        //    its grade level, effective from today and open-ended. Idempotent: skip
        //    if an active (unended) assignment already exists for this grade/topic.
        var existingAssignments = await assignmentRepository
            .ListByGradeLevelAsync(command.GradeLevelId, today, cancellationToken);

        if (!existingAssignments.Any(a => a.TopicId == subject.Id))
        {
            var assignment = GradeTopicAssignment.Create(
                    command.GradeLevelId,
                    subject.Id,
                    today)
                .WithTenant(tenantProvider);

            await assignmentRepository.AddAsync(assignment, cancellationToken);
            assignment.ClearDomainEvents();
            logger.LogInformation(
                "GradeTopicAssignment created for grade {GradeLevelId}, topic {TopicId}, from {StartDate}",
                command.GradeLevelId, subject.Id, today);
        }
        else
        {
            logger.LogInformation(
                "GradeTopicAssignment already active for grade {GradeLevelId}, topic {TopicId} — skipping",
                command.GradeLevelId, subject.Id);
        }

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