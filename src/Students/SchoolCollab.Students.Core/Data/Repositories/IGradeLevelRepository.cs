using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IGradeLevelRepository
{
    Task<GradeLevel?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(GradeLevel gradeLevel, CancellationToken cancellationToken = default);
    Task UpdateAsync(GradeLevel gradeLevel, CancellationToken cancellationToken = default);
    Task<GradeLevelDto[]> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the grade level with the given coded-value id, or null. Backs the
    /// find-or-create flow (§6.3) and the <c>GET /grade-levels/by-coded-value/{id}</c> read.
    /// </summary>
    Task<GradeLevel?> GetByCodedValueIdAsync(Guid codedValueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts <paramref name="candidate"/>, or — when a concurrent writer already
    /// materialized the same (tenant, coded_value) pair (unique index
    /// <c>ix_grade_levels_tenant_coded_value_id</c>) — evicts the losing tracked
    /// insert and returns the WINNING row instead. Closes the TOCTOU race in the
    /// enroll-time materialization path where two concurrent first-time enrolls
    /// both observe a missing GradeLevel row.
    /// </summary>
    Task<GradeLevel> AddOrReuseAsync(GradeLevel candidate, CancellationToken cancellationToken = default);
}