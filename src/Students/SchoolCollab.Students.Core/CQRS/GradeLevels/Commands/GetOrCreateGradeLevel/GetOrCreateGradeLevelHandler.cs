using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.GetOrCreateGradeLevel;

/// <summary>
/// Find-or-create by <see cref="GetOrCreateGradeLevel.CodedValueId"/>. Reuses the
/// existing grade level (updating mirrored Name/Level/DisplayOrder) or creates a
/// new one, then returns a <see cref="GradeLevelDto"/>. Safe under the unique index
/// on <c>CodedValueId</c> (§5.7). Invalidates the <c>students</c> cache tag.
/// </summary>
public sealed class GetOrCreateGradeLevelHandler(
    IGradeLevelRepository repository,
    HybridCache cache,
    ITenantProvider tenantProvider,
    ILogger<GetOrCreateGradeLevelHandler> logger) : ICommandHandler<GetOrCreateGradeLevel, GradeLevelDto>
{
    public async Task<GradeLevelDto> HandleAsync(
        GetOrCreateGradeLevel command,
        CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be created with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(GetOrCreateGradeLevel), typeof(GradeLevel));

        logger.LogDebug("Handling GetOrCreateGradeLevel for CodedValueId {Id}", command.CodedValueId);

        var existing = await repository.GetByCodedValueIdAsync(command.CodedValueId, cancellationToken);

        GradeLevel gradeLevel;
        bool created;

        if (existing is not null)
        {
            // Preserve the existing enrollment-validation fields on reuse — the
            // find-or-create syncs only the mirrored Name/Level/DisplayOrder from
            // the coded value. Validation rules (MinAge/MaxAge/AllowedGender) are
            // managed via the Edit page (UpdateGradeLevel), not the wizard.
            existing.Update(command.Level, command.Name, command.DisplayOrder,
                existing.MinAge, existing.MaxAge, existing.AllowedGenderCodedValueId);
            await repository.UpdateAsync(existing, cancellationToken);
            gradeLevel = existing;
            created = false;
            logger.LogInformation("GradeLevel {Id} reused for CodedValueId {CodedValueId} (mirrored fields updated)",
                gradeLevel.Id, command.CodedValueId);
        }
        else
        {
            gradeLevel = GradeLevel.Create(
                command.CodedValueId, command.Level, command.Name, command.DisplayOrder,
                command.MinAge, command.MaxAge, command.AllowedGenderCodedValueId)
                .WithTenant(tenantProvider);
            await repository.AddAsync(gradeLevel, cancellationToken);
            created = true;
            logger.LogInformation("GradeLevel {Id} created for CodedValueId {CodedValueId}",
                gradeLevel.Id, command.CodedValueId);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);
        gradeLevel.ClearDomainEvents();

        return new GradeLevelDto(
            gradeLevel.Id,
            gradeLevel.CodedValueId,
            gradeLevel.Level,
            gradeLevel.Name,
            gradeLevel.DisplayOrder,
            TopicCount: 0,
            StudentCount: 0,
            gradeLevel.CreatedAt,
            gradeLevel.UpdatedAt,
            gradeLevel.MinAge,
            gradeLevel.MaxAge,
            gradeLevel.AllowedGenderCodedValueId);
    }
}