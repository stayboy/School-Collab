using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

/// <summary>Persistence for <see cref="Teacher"/> and its subject/grade-level links (spec §4.12).</summary>
public interface ITeacherRepository
{
    Task<Teacher?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Teacher?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Teacher teacher, CancellationToken cancellationToken = default);
    Task UpdateAsync(Teacher teacher, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(Teacher teacher, CancellationToken cancellationToken = default);

    Task AddSubjectAsync(TeacherSubject link, CancellationToken cancellationToken = default);
    Task RemoveSubjectAsync(Guid teacherId, Guid subjectId, CancellationToken cancellationToken = default);
    Task AddGradeLevelAsync(TeacherGradeLevel link, CancellationToken cancellationToken = default);
    Task RemoveGradeLevelAsync(Guid teacherId, Guid gradeLevelId, CancellationToken cancellationToken = default);
}
