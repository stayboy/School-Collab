using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.TopicAssignments;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Commands.AssignStudentTopic;

public sealed class AssignStudentTopicHandler(
    IStudentTopicAssignmentRepository repository,
    IPeriodRepository periodRepository,
    IActivePeriodProvider activePeriodProvider,
    HybridCache cache,
    ILogger<AssignStudentTopicHandler> logger) : ICommandHandler<AssignStudentTopic, Guid>
{
    public async Task<Guid> HandleAsync(AssignStudentTopic command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling AssignStudentTopic for student {StudentId} topic {TopicId}", command.StudentId, command.TopicId);

        // FR-H13 (Rev. 3): the assignment must record the period context of its
        // creation — the active AcademicYear (PeriodId) + the active sub-period
        // (SubPeriodId). No active year ⇒ reject (FR-A3-style).
        var activeYear = await activePeriodProvider.GetActiveAcademicYearAsync(cancellationToken);
        if (activeYear is null)
        {
            throw new PeriodNotOpenException(
                "Cannot assign a student topic: no active academic year is open for this tenant.");
        }

        // Source-scope validation (FR-H13 / AC-H12): a nonexistent period, or a
        // Term/Semester outside the active year, is rejected (422).
        await TopicAssignmentPeriodValidator.ValidateGradePeriodAsync(
            command.PeriodId, periodRepository, cancellationToken);

        // Year match (FR-A3-style): a caller-provided top-level academic year other
        // than the active year is rejected. A Term/Semester of the active year is a
        // valid term/semester-scoped source (already validated above).
        var sourcePeriod = await periodRepository.GetAsync(command.PeriodId, cancellationToken);
        if (sourcePeriod is not null
            && sourcePeriod.ParentPeriodId is null
            && sourcePeriod.Id != activeYear.Id)
        {
            throw new PeriodNotOpenException(
                $"Assignment targets period '{command.PeriodId}' but the active academic year is '{activeYear.Id}'. " +
                "Assignments must target the tenant's active academic year.");
        }

        // Stamp (server-resolved, never caller-chosen): PeriodId = the active year;
        // SubPeriodId = the active sub-period (null in the gap state / None-division
        // year, EC-H6). Reusable by the future bridge-derivation path.
        var activeSubPeriod = await activePeriodProvider.GetActiveSubPeriodAsync(cancellationToken);
        var assignment = CreateAssignment(
            command.StudentId, command.TopicId, activeYear.Id, activeSubPeriod?.Id,
            command.IsOverride, command.SourceType);

        await repository.AddAsync(assignment, cancellationToken);
        assignment.ClearDomainEvents();
        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("StudentTopicAssignment {Id} created", assignment.Id);
        return assignment.Id;
    }

    /// <summary>
    /// Builds the assignment with the server-resolved creation stamp (FR-H13):
    /// <paramref name="activeYearId"/> is the active AcademicYear; a null
    /// <paramref name="activeSubPeriodId"/> records the gap state / None-division
    /// year. Kept as a static helper so the future bridge-derivation assign path
    /// (subject-to-topic §12 Q3) can reuse the identical stamp.
    /// </summary>
    private static StudentTopicAssignment CreateAssignment(
        Guid studentId,
        Guid topicId,
        Guid activeYearId,
        Guid? activeSubPeriodId,
        bool isOverride,
        SubjectAssignmentSource sourceType)
        => StudentTopicAssignment.Create(studentId, topicId, activeYearId, isOverride, sourceType, activeSubPeriodId);
}
