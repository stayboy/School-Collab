using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

/// <summary>
/// Repository for the grade↔stream bridge
/// (<see cref="GradeStreamAssignment"/>).
/// </summary>
public interface IGradeStreamAssignmentRepository
{
    /// <summary>Loads one bridge row by id (tenant-filtered).</summary>
    Task<GradeStreamAssignment?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a bridge row, or returns the winning row when a concurrent insert
    /// already took the unique (tenant, grade, stream) slot — the same race-safe
    /// semantics the enrollment inserts use.
    /// </summary>
    Task<GradeStreamAssignment> AddOrReuseAsync(GradeStreamAssignment candidate, CancellationToken cancellationToken = default);

    /// <summary>Lists the bridge rows offering a stream for the given grade level.
    /// Ordered by the bridge's own <c>DisplayOrder</c> — the ordering authority for
    /// the grade's stream list (the coded value's order is the catalogue's).</summary>
    Task<GradeStreamAssignment[]> ListByGradeLevelAsync(Guid gradeLevelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the grade's bridge rows <b>tracked</b>, for a reorder that mutates
    /// several rows in one save. The listing twin is <c>AsNoTracking</c> and cannot
    /// be used there.
    /// </summary>
    Task<GradeStreamAssignment[]> ListByGradeLevelForUpdateAsync(Guid gradeLevelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The append-at-end position for a newly created bridge row: the grade's
    /// highest <c>DisplayOrder</c> + 1, or 0 when the grade offers no streams yet.
    /// </summary>
    Task<int> GetNextDisplayOrderAsync(Guid gradeLevelId, CancellationToken cancellationToken = default);

    /// <summary>Persists tracked mutations (a reorder changes several rows at once).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the grade already offers the given stream coded value.</summary>
    Task<bool> ExistsAsync(Guid gradeLevelId, Guid streamCodedValueId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a bridge row (the coded value itself is untouched — it stays in the catalogue).</summary>
    Task DeleteAsync(GradeStreamAssignment assignment, CancellationToken cancellationToken = default);
}
