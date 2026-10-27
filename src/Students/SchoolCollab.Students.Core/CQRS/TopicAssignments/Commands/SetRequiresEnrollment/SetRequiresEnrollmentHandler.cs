using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.SetRequiresEnrollment;

/// <summary>
/// Flips a grade subject's <see cref="TopicAssignment.RequiresEnrollment"/> flag and, on a
/// <c>false → true</c> flip, grandfathers the current audience (FR-ER-7).
/// <para><b>One transaction, one SaveChanges.</b> The flag update and every materialised
/// <see cref="StudentTopicAssignment"/> ride one <see cref="IUnitOfWork{TContext}.ExecuteAsync"/>
/// action and a single <c>SaveChangesAsync</c> (FR-ER-10): a partial grandfather — flag set,
/// some students missing — is worse than none, because it reproduces the silent-truncation
/// failure the spec accepts only for the steady state. This is deliberately <i>not</i> a loop
/// over <c>AssignStudentTopicHandler</c>: its repository's <c>AddAsync</c> saves per entity, so
/// a mid-loop failure would commit the earlier rows.</para>
/// <para>Idempotent: a student already holding a row for the active academic year is not
/// re-inserted (the <c>(tenant, student, topic, period)</c> unique index would otherwise
/// abort the transaction).</para>
/// <para><c>true → false</c> only flips the flag — the rows stay as inert history (FR-ER-9);
/// they are per-year, so they neither shadow a later re-enrolment nor need deleting.</para>
/// <para>An active academic year is required: enrolment rows are year-keyed (FR-ER-3), so
/// without one there is neither an audience to grandfather nor a row to report on.</para>
/// </summary>
public sealed class SetRequiresEnrollmentHandler(
    StudentsDbContext db,
    IUnitOfWork<StudentsDbContext> unitOfWork,
    IActivePeriodProvider activePeriodProvider,
    HybridCache cache,
    ILogger<SetRequiresEnrollmentHandler> logger)
    : ICommandHandler<SetRequiresEnrollment, TopicEnrollmentCountsDto>
{
    public async Task<TopicEnrollmentCountsDto> HandleAsync(
        SetRequiresEnrollment command,
        CancellationToken cancellationToken = default)
    {
        var counts = await unitOfWork.ExecuteAsync(async (ctx, ct) =>
        {
            // Grade bridge rows only: the flag's audience and the grandfather exist for a
            // grade. An unknown, other-tenant, or activity-group id is a 404 at the route —
            // the same discrimination SetGradeTopicOrder makes.
            var assignment = await ctx.GradeTopicAssignments
                .FirstOrDefaultAsync(x => x.Id == command.AssignmentId, ct)
                ?? throw new KeyNotFoundException(
                    $"Grade topic assignment '{command.AssignmentId}' was not found.");

            var activeYear = await activePeriodProvider.GetActiveAcademicYearAsync(ct)
                ?? throw new PeriodNotOpenException(
                    $"Cannot change the enrollment requirement of subject '{assignment.TopicId}': no active academic year.");

            var audience = await ctx.ActiveGradeRosterAsync(assignment.GradeLevelId, activeYear.Id, ct);

            var enrolledStudentIds = await ctx.StudentTopicAssignments
                .Where(x => x.TopicId == assignment.TopicId
                            && x.PeriodId == activeYear.Id
                            && audience.Contains(x.StudentId))
                .Select(x => x.StudentId)
                .ToArrayAsync(ct);
            var enrolled = enrolledStudentIds.ToHashSet();

            // Read BEFORE the flip so the counts describe the state the switch is changing
            // (FR-ER-8): the audience, who already held a row, and who did not.
            var result = new TopicEnrollmentCountsDto(
                assignment.Id,
                command.Value,
                audience.Length,
                enrolled.Count,
                audience.Length - enrolled.Count);

            // The pre-flip state, captured before the setter: FR-ER-7 grandfathers a subject that
            // currently enrols by implication. Gating on `command.Value` alone would re-grandfather
            // on a redundant true → true PUT, materialising rows for every grade member added since
            // the first flip — silently enrolling students who must be enrolled explicitly.
            var wasImplied = !assignment.RequiresEnrollment;

            assignment.SetRequiresEnrollment(command.Value);

            if (command.Value && wasImplied)
            {
                var activeSubPeriod = await activePeriodProvider.GetActiveSubPeriodAsync(ct);

                foreach (var studentId in audience.Where(id => !enrolled.Contains(id)))
                {
                    ctx.StudentTopicAssignments.Add(StudentTopicAssignment.Create(
                        studentId,
                        assignment.TopicId,
                        activeYear.Id,
                        isOverride: false,
                        SubjectAssignmentSource.GradeAssignment,
                        activeSubPeriod?.Id));
                }
            }

            await ctx.SaveChangesAsync(ct);
            return result;
        }, cancellationToken);

        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation(
            "TopicAssignment {Id} RequiresEnrollment set to {Value} (audience {Audience}, enrolled {Enrolled}, unenrolled {Unenrolled})",
            counts.AssignmentId, counts.RequiresEnrollment,
            counts.AudienceCount, counts.EnrolledCount, counts.UnenrolledCount);

        return counts;
    }
}
