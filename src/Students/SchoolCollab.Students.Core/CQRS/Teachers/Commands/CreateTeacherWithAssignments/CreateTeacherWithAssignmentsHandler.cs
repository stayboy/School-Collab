using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.CreateTeacherWithAssignments;

/// <summary>
/// Atomically creates a teacher with its grade and activity assignments as a
/// single unit of work. The teacher row, its qualifications, and every grade /
/// activity link are written in one EF Core transaction and committed together.
/// If any step fails (missing grade/activity, duplicate link, transient DB
/// fault), the entire batch is rolled back — no orphaned teacher, no partial
/// assignments.
/// </summary>
public sealed class CreateTeacherWithAssignmentsHandler(
    IUnitOfWork<StudentsDbContext> uow,
    IEntityCodeGenerator entityCodeGenerator,
    StudentsDbContext db,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<CreateTeacherWithAssignmentsHandler> logger)
    : ICommandHandler<CreateTeacherWithAssignments, Guid>
{
    public async Task<Guid> HandleAsync(
        CreateTeacherWithAssignments command,
        CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(CreateTeacherWithAssignments), typeof(Teacher));

        // Pre-validate every reference id BEFORE tracking anything, so a bad
        // grade/activity id fails fast with a domain exception (mapped to 4xx)
        // rather than surfacing as a raw DB error mid-transaction.
        await ValidateReferencesAsync(command, cancellationToken);

        return await uow.ExecuteAsync(async (ctx, ct) =>
        {
            // Spec §4.5: auto-generate the staff number before constructing the entity.
            var staffNumber = await entityCodeGenerator.GenerateAsync("STAFF_CODE", ct);

            var teacher = Teacher.Create(
                    command.TitleCodedValueId,
                    command.FirstName,
                    command.LastName,
                    command.DisplayName,
                    staffNumber: staffNumber,
                    genderCodedValueId: command.GenderCodedValueId,
                    dateOfBirth: command.DateOfBirth,
                    levelOfEducationCodedValueId: command.LevelOfEducationCodedValueId)
                .WithTenant(tenantProvider);
            ctx.Teachers.Add(teacher);

            foreach (var q in command.QualificationCodedValueIds ?? [])
                ctx.TeacherQualifications.Add(
                    TeacherQualification.Create(teacher.Id, q).WithTenant(tenantProvider));

            foreach (var g in command.GradeAssignments ?? [])
                ctx.TeacherGradeLevels.Add(
                    TeacherGradeLevel.Create(teacher.Id, g.GradeLevelId, g.SubjectId, g.RoleCodedValueId)
                        .WithTenant(tenantProvider));

            foreach (var a in command.ActivityAssignments ?? [])
                ctx.TeacherActivityAssignments.Add(
                    TeacherActivityAssignment.Create(teacher.Id, a.ActivityGroupId, a.RoleCodedValueId, a.GradeLevelIds)
                        .WithTenant(tenantProvider));

            // Single commit — the unit of work commits the transaction only if
            // this returns without throwing.
            await ctx.SaveChangesAsync(ct);

            logger.LogInformation(
                "Teacher {Id} created with staff number {StaffNumber} for tenant {TenantId} with {GradeCount} grade and {ActivityCount} activity assignments",
                teacher.Id, teacher.StaffNumber, teacher.TenantId,
                command.GradeAssignments?.Length ?? 0, command.ActivityAssignments?.Length ?? 0);

            return teacher.Id;
        }, cancellationToken);
    }

    private async Task ValidateReferencesAsync(
        CreateTeacherWithAssignments command,
        CancellationToken cancellationToken)
    {
        var gradeIds = (command.GradeAssignments ?? [])
            .Select(g => g.GradeLevelId)
            .Concat((command.ActivityAssignments ?? [])
                .SelectMany(a => a.GradeLevelIds ?? []))
            .Distinct()
            .ToArray();

        if (gradeIds.Length > 0)
        {
            var existingGradeIds = await db.GradeLevels
                .Where(g => gradeIds.Contains(g.Id))
                .Select(g => g.Id)
                .ToArrayAsync(cancellationToken);
            var missing = gradeIds.Except(existingGradeIds).ToArray();
            if (missing.Length > 0)
                throw new GradeLevelNotFoundException(missing[0]);
        }

        var activityIds = (command.ActivityAssignments ?? [])
            .Select(a => a.ActivityGroupId)
            .Distinct()
            .ToArray();
        if (activityIds.Length > 0)
        {
            var existingActivityIds = await db.ActivityGroups
                .Where(a => activityIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToArrayAsync(cancellationToken);
            var missing = activityIds.Except(existingActivityIds).ToArray();
            if (missing.Length > 0)
                throw new ActivityGroupNotFoundException(missing[0]);
        }

        // Reject duplicate grade links within the batch (same grade + subject).
        var gradeLinks = command.GradeAssignments ?? [];
        for (var i = 0; i < gradeLinks.Length; i++)
        {
            for (var j = i + 1; j < gradeLinks.Length; j++)
            {
                if (gradeLinks[i].GradeLevelId == gradeLinks[j].GradeLevelId
                    && gradeLinks[i].SubjectId == gradeLinks[j].SubjectId)
                {
                    throw new TeacherLinkAlreadyExistsException(
                        Guid.Empty, gradeLinks[i].GradeLevelId);
                }
            }
        }
    }
}
