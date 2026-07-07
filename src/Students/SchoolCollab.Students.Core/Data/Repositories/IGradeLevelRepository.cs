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
}