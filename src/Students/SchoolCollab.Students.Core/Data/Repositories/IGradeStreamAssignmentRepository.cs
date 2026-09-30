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

    /// <summary>Lists the bridge rows offering a stream for the given grade level.</summary>
    Task<GradeStreamAssignment[]> ListByGradeLevelAsync(Guid gradeLevelId, CancellationToken cancellationToken = default);

    /// <summary>True when the grade already offers the given stream coded value.</summary>
    Task<bool> ExistsAsync(Guid gradeLevelId, Guid streamCodedValueId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a bridge row (the coded value itself is untouched — it stays in the catalogue).</summary>
    Task DeleteAsync(GradeStreamAssignment assignment, CancellationToken cancellationToken = default);
}
