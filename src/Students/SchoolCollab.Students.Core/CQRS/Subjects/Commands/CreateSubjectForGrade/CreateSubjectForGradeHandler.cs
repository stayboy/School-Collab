using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubjectForGrade;

/// <summary>
/// Creates (or reuses) a <see cref="Subject"/> and a
/// <see cref="GradeSubjectAssignment"/> for the <b>current period</b> (§8.1).
/// </summary>
public sealed class CreateSubjectForGradeHandler(
    ISubjectRepository subjectRepository,
    IGradeSubjectAssignmentRepository assignmentRepository,
    IGradeLevelRepository gradeLevelRepository,
    IPeriodRepository periodRepository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreateSubjectForGradeHandler> logger) : ICommandHandler<CreateSubjectForGrade, SubjectDto>
{
    public async Task<SubjectDto> HandleAsync(
        CreateSubjectForGrade command,
        CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreateSubjectForGrade), typeof(Subject));

        logger.LogDebug(
            "Handling CreateSubjectForGrade for grade {GradeLevelId}, code {Code}",
            command.GradeLevelId, command.Code);

        // 1. Verify the grade level exists.
        var gradeLevel = await gradeLevelRepository.GetAsync(command.GradeLevelId, cancellationToken)
            ?? throw new GradeLevelNotFoundException(command.GradeLevelId);

        // 2. Derive the current period server-side (§5.3). If there is no current
        //    period, we cannot create a period-scoped assignment.
        var currentPeriod = await periodRepository.GetCurrentPeriodAsync(cancellationToken)
            ?? throw new NoCurrentPeriodException(
                "No period covers today. Create a period whose date range includes today before assigning subjects to a grade level.");

        // 3. Find-or-create the Subject.
        //    - If a CodedValueId is provided, look up by it first (the operational
        //      peer of GradeLevel — stable reporting key, §3.2).
        //    - Otherwise fall back to lookup by Code.
        //    - If neither finds an existing Subject, create a new one.
        Subject subject;
        bool subjectCreated = false;

        if (command.CodedValueId.HasValue)
        {
            subject = await subjectRepository.GetByCodedValueIdAsync(command.CodedValueId.Value, cancellationToken);
        }
        else
        {
            subject = await subjectRepository.GetByCodeAsync(command.Code, cancellationToken);
        }

        if (subject is not null)
        {
            // Reuse the existing subject — update mirrored Name/DisplayOrder.
            subject.Update(command.Name, command.DisplayOrder);
            await subjectRepository.UpdateAsync(subject, cancellationToken);
            logger.LogInformation("Subject {Id} reused for grade {GradeLevelId}", subject.Id, command.GradeLevelId);
        }
        else
        {
            // Verify the code is not already taken (only relevant when we looked
            // up by CodedValueId and didn't find it but the code is in use).
            if (await subjectRepository.ExistsByCodeAsync(command.Code, cancellationToken))
                throw new DuplicateSubjectCodeException(command.Code);

            var codedValueId = command.CodedValueId ?? Guid.NewGuid();
            subject = Subject.Create(codedValueId, command.Code, command.Name, command.DisplayOrder)
                .WithTenant(tenantProvider);
            await subjectRepository.AddAsync(subject, cancellationToken);
            subjectCreated = true;
            logger.LogInformation("Subject {Id} created for grade {GradeLevelId}", subject.Id, command.GradeLevelId);
        }

        // 4. Create the GradeSubjectAssignment for the current period (idempotent:
        //    skip if one already exists for this grade/subject/period).
        var existingAssignments = await assignmentRepository
            .ListByGradeLevelAsync(command.GradeLevelId, currentPeriod.Id, cancellationToken);

        if (!existingAssignments.Any(a => a.SubjectId == subject.Id))
        {
            var assignment = GradeSubjectAssignment.Create(
                command.GradeLevelId,
                subject.Id,
                currentPeriod.Id)
                .WithTenant(tenantProvider);

            await assignmentRepository.AddAsync(assignment, cancellationToken);
            assignment.ClearDomainEvents();
            logger.LogInformation(
                "GradeSubjectAssignment created for grade {GradeLevelId}, subject {SubjectId}, period {PeriodId}",
                command.GradeLevelId, subject.Id, currentPeriod.Id);
        }
        else
        {
            logger.LogInformation(
                "GradeSubjectAssignment already exists for grade {GradeLevelId}, subject {SubjectId}, period {PeriodId} — skipping",
                command.GradeLevelId, subject.Id, currentPeriod.Id);
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