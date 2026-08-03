using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IGradeSubjectAssignmentRepository
{
    Task<GradeSubjectAssignment?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(GradeSubjectAssignment assignment, CancellationToken cancellationToken = default);
    Task EndAsync(GradeSubjectAssignment assignment, DateOnly endDate, CancellationToken cancellationToken = default);
    Task<GradeSubjectAssignmentDto[]> ListByGradeLevelAsync(Guid gradeLevelId, DateOnly effectiveDate, CancellationToken cancellationToken = default);
}