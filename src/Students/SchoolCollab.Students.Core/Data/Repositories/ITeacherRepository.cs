using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

/// <summary>Persistence for <see cref="Teacher"/> and its topic/grade-level links (spec §4.12).
/// v4: grade-scoped subject (topic) rows on <see cref="TeacherGradeLevel"/> + activity assignments.</summary>
public interface ITeacherRepository
{
    Task<Teacher?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Teacher?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Teacher teacher, CancellationToken cancellationToken = default);
    Task UpdateAsync(Teacher teacher, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(Teacher teacher, CancellationToken cancellationToken = default);

    Task AddQualificationAsync(TeacherQualification link, CancellationToken cancellationToken = default);
    Task<Guid[]> GetQualificationCodedValueIdsAsync(Guid teacherId, CancellationToken cancellationToken = default);
    Task RemoveQualificationAsync(Guid teacherId, Guid codedValueId, CancellationToken cancellationToken = default);

    Task AddGradeLevelAsync(TeacherGradeLevel link, CancellationToken cancellationToken = default);
    Task UpdateGradeLevelAsync(TeacherGradeLevel link, CancellationToken cancellationToken = default);
    Task<TeacherGradeLevel?> GetGradeLevelLinkAsync(Guid teacherId, Guid gradeLevelId, CancellationToken cancellationToken = default);
    Task RemoveGradeLevelAsync(Guid teacherId, Guid gradeLevelId, CancellationToken cancellationToken = default);

    // v4 grade-scoped subject rows (a row = grade + optional subject + role).
    Task<TeacherGradeLevel?> GetGradeLevelLinkByIdAsync(Guid rowId, CancellationToken cancellationToken = default);
    Task<TeacherGradeLevel?> GetGradeLevelLinkAsync(Guid teacherId, Guid gradeLevelId, Guid? topicId, CancellationToken cancellationToken = default);
    Task<TeacherGradeLevel[]> GetGradeLevelLinksAsync(Guid teacherId, CancellationToken cancellationToken = default);
    Task RemoveGradeLevelRowAsync(Guid rowId, CancellationToken cancellationToken = default);

    // v4 activity assignments (activity + role + optional grades).
    Task AddActivityAssignmentAsync(TeacherActivityAssignment link, CancellationToken cancellationToken = default);
    Task UpdateActivityAssignmentAsync(TeacherActivityAssignment link, CancellationToken cancellationToken = default);
    Task<TeacherActivityAssignment?> GetActivityAssignmentByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TeacherActivityAssignment[]> GetActivityAssignmentsAsync(Guid teacherId, CancellationToken cancellationToken = default);
    Task RemoveActivityAssignmentAsync(Guid id, CancellationToken cancellationToken = default);
}
