using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Core.CQRS.GradeStreams.Commands.AssignGradeStream;

/// <summary>
/// Assigns a stream to a grade level by inserting a
/// <see cref="GradeStreamAssignment"/> bridge row.
/// </summary>
/// <remarks>
/// <para><b>Uniqueness moved here.</b> A grade must not hold two streams with the
/// same <c>streamVersion</c> label. That rule used to be enforced by the Settings
/// coded-value attribute write; it is now enforced at assignment time, because the
/// bridge row — not the coded value's <c>gradeLevel</c> attribute — is the link.
/// The existing rows' versions are read from the override-resolving
/// <c>by-parent</c> catalogue hop, never duplicated onto the bridge row.</para>
/// <para><b>Idempotent.</b> Re-assigning the same (grade, stream) pair returns the
/// existing bridge row id: the repository reuses the winner of the unique-index
/// race.</para>
/// <para>A stream with no (or an empty) <c>streamVersion</c> never trips the
/// uniqueness rule — a brand-new user-created stream has no version label yet.</para>
/// </remarks>
public sealed class AssignGradeStreamHandler(
    IGradeStreamAssignmentRepository repository,
    IGradeLevelRepository gradeLevelRepository,
    ICodedValuesApiClient codedValuesApi,
    HybridCache cache,
    ILogger<AssignGradeStreamHandler> logger) : ICommandHandler<AssignGradeStream, Guid>
{
    public async Task<Guid> HandleAsync(
        AssignGradeStream command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Handling AssignGradeStream grade {GradeLevelId} stream {StreamCodedValueId}",
            command.GradeLevelId, command.StreamCodedValueId);

        // The grade must exist for this tenant (the tenant filter applies).
        var gradeLevel = await gradeLevelRepository.GetAsync(command.GradeLevelId, cancellationToken)
            ?? throw new GradeLevelNotFoundException(command.GradeLevelId);

        var stream = await codedValuesApi.GetByIdAsync(command.StreamCodedValueId, cancellationToken)
            ?? throw new StreamGradeMismatchException(command.StreamCodedValueId, gradeLevel.Id);

        var incomingVersion = stream.Attributes
            .FirstOrDefault(a => a.Key == GradeStreamAssignment.StreamVersionAttributeKey)?.Value;

        if (!string.IsNullOrWhiteSpace(incomingVersion))
        {
            await EnsureVersionIsUniqueAsync(
                gradeLevel.Id, command.StreamCodedValueId, incomingVersion, cancellationToken);
        }

        var assignment = await repository.AddOrReuseAsync(
            GradeStreamAssignment.Create(gradeLevel.Id, command.StreamCodedValueId), cancellationToken);

        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation(
            "Stream {StreamCodedValueId} offered by grade {GradeLevelId} (assignment {AssignmentId})",
            command.StreamCodedValueId, gradeLevel.Id, assignment.Id);
        return assignment.Id;
    }

    /// <summary>
    /// Rejects the assignment when another stream already linked to this grade
    /// carries the same non-empty version label. The versions of the ALREADY
    /// linked streams are read in one override-resolving <c>by-parent</c> hop and
    /// filtered down to the bridge's stream ids.
    /// </summary>
    private async Task EnsureVersionIsUniqueAsync(
        Guid gradeLevelId,
        Guid incomingStreamCodedValueId,
        string incomingVersion,
        CancellationToken cancellationToken)
    {
        var existing = await repository.ListByGradeLevelAsync(gradeLevelId, cancellationToken);
        var existingIds = existing
            .Select(x => x.StreamCodedValueId)
            .Where(x => x != incomingStreamCodedValueId)
            .ToHashSet();
        if (existingIds.Count == 0) return;

        var catalogue = await codedValuesApi.GetChildrenByParentCodeAsync(
            GradeStreamAssignment.CatalogueParentCode, cancellationToken);

        foreach (var cv in catalogue)
        {
            if (!existingIds.Contains(cv.Id)) continue;

            var version = cv.Attributes
                .FirstOrDefault(a => a.Key == GradeStreamAssignment.StreamVersionAttributeKey)?.Value;
            if (string.IsNullOrWhiteSpace(version)) continue;

            if (string.Equals(version.Trim(), incomingVersion.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new DuplicateStreamAssignmentException(gradeLevelId, incomingVersion, cv.Id);
            }
        }
    }
}
