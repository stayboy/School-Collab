using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

/// <summary>
/// Repository for grade-level topic assignments (TPH subtype
/// <see cref="Domain.GradeTopicAssignment"/>).
/// </summary>
public interface IGradeTopicAssignmentRepository : ITopicAssignmentRepository
{
    Task<TopicAssignmentDto[]> ListByGradeLevelAsync(Guid gradeLevelId, DateOnly effectiveDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the grade's <b>effective-on-<paramref name="effectiveDate"/></b> bridge rows
    /// <b>tracked</b>, for a reorder that mutates several rows in one save. The set is
    /// the same one the curriculum read lists and the reorder's out-of-range bound is
    /// computed from — an assignment outside its effective window is not visible to
    /// the grid and must not be reorderable.
    /// </summary>
    Task<GradeTopicAssignment[]> ListByGradeLevelForUpdateAsync(Guid gradeLevelId, DateOnly effectiveDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// The append-at-end position for a newly created bridge row: the grade's
    /// highest <c>DisplayOrder</c> + 1, or 0 when the grade has no assignments yet.
    /// </summary>
    Task<int> GetNextDisplayOrderAsync(Guid gradeLevelId, CancellationToken cancellationToken = default);

    /// <summary>Persists tracked mutations (a reorder changes several rows at once).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
