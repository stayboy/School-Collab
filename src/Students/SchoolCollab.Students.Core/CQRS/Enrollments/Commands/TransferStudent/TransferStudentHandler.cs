using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Services;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Commands.TransferStudent;

public sealed class TransferStudentHandler(
    IStudentEnrollmentRepository repository,
    IGradeLevelRepository gradeLevelRepository,
    ICodedValuesApiClient codedValuesApi,
    StudentsDbContext db,
    IActorAccessor actorAccessor,
    IIntegrationEventPublisher publisher,
    HybridCache cache,
    ILogger<TransferStudentHandler> logger) : ICommandHandler<TransferStudent>
{
    public async Task HandleAsync(TransferStudent command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling TransferStudent {EnrollmentId}", command.EnrollmentId);

        var enrollment = await repository.GetAsync(command.EnrollmentId, cancellationToken)
            ?? throw new InvalidOperationException($"Enrollment with ID '{command.EnrollmentId}' not found.");

        // FR-9: strand validation. If a new strand is provided, it must match the
        // new grade. If null, the strand is cleared (grade transfer).
        if (command.NewGradeStrandCodedValueId is { } strandId)
        {
            await ValidateStrandAsync(command.NewGradeLevelId, strandId, cancellationToken);
        }

        var fromGradeLevelId = enrollment.GradeLevelId;
        enrollment.Transfer(command.NewGradeLevelId, command.TransferDate, command.Reason, command.NewGradeStrandCodedValueId);

        // Audit the transfer in the same transaction as the enrollment update
        // (the repository's SaveChangesAsync flushes both tracked changes).
        new StudentTransferAuditor(actorAccessor).Record(
            db,
            enrollment.TenantId,
            enrollment.StudentId,
            fromGradeLevelId,
            command.NewGradeLevelId,
            enrollment.PeriodId,
            command.Reason);

        try
        {
            await repository.UpdateAsync(enrollment, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("StudentEnrollment", enrollment.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        foreach (var evt in enrollment.DomainEvents.OfType<StudentTransferredEvent>())
        {
            await publisher.EnqueueAsync(new StudentTransferred(
                enrollment.StudentId,
                enrollment.PeriodId,
                fromGradeLevelId,
                evt.NewGradeLevelId,
                evt.NewGradeStrandCodedValueId,
                DateTimeOffset.UtcNow), cancellationToken);
        }

        enrollment.ClearDomainEvents();

        logger.LogInformation("Student {StudentId} transferred from grade {FromGrade} to {ToGrade}", enrollment.StudentId, fromGradeLevelId, command.NewGradeLevelId);
    }

    private async Task ValidateStrandAsync(Guid gradeLevelId, Guid strandCodedValueId, CancellationToken cancellationToken)
    {
        var gradeLevel = await gradeLevelRepository.GetAsync(gradeLevelId, cancellationToken)
            ?? throw new GradeLevelNotFoundException(gradeLevelId);
        var gradeCodedValueId = gradeLevel.CodedValueId;

        var strand = await codedValuesApi.GetByIdAsync(strandCodedValueId, cancellationToken)
            ?? throw new GradeStrandGradeMismatchException(strandCodedValueId, gradeLevelId);

        var gradeLevelAttr = strand.Attributes
            .FirstOrDefault(a => a.Key == "gradeLevel");
        if (gradeLevelAttr is null)
        {
            throw new GradeStrandGradeMismatchException(strandCodedValueId, gradeLevelId);
        }

        if (!Guid.TryParse(gradeLevelAttr.Value, out var strandGradeCodedValueId)
            || strandGradeCodedValueId != gradeCodedValueId)
        {
            throw new GradeStrandGradeMismatchException(strandCodedValueId, gradeLevelId);
        }
    }
}