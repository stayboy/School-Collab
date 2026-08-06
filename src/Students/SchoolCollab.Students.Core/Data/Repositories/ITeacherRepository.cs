using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

/// <summary>Persistence for <see cref="Teacher"/> and its topic/grade-level links (spec §4.12).</summary>
public interface ITeacherRepository
{
    Task<Teacher?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Teacher?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Teacher teacher, CancellationToken cancellationToken = default);
    Task UpdateAsync(Teacher teacher, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(Teacher teacher, CancellationToken cancellationToken = default);

    Task AddTopicAsync(TeacherTopic link, CancellationToken cancellationToken = default);
    Task<TeacherTopic?> GetTopicLinkAsync(Guid teacherId, Guid topicId, CancellationToken cancellationToken = default);
    Task UpdateTopicAsync(TeacherTopic link, CancellationToken cancellationToken = default);
    Task RemoveTopicAsync(Guid teacherId, Guid topicId, CancellationToken cancellationToken = default);

    Task AddQualificationAsync(TeacherQualification link, CancellationToken cancellationToken = default);
    Task<Guid[]> GetQualificationCodedValueIdsAsync(Guid teacherId, CancellationToken cancellationToken = default);
    Task RemoveQualificationAsync(Guid teacherId, Guid codedValueId, CancellationToken cancellationToken = default);

    Task AddGradeLevelAsync(TeacherGradeLevel link, CancellationToken cancellationToken = default);
    Task UpdateGradeLevelAsync(TeacherGradeLevel link, CancellationToken cancellationToken = default);
    Task<TeacherGradeLevel?> GetGradeLevelLinkAsync(Guid teacherId, Guid gradeLevelId, CancellationToken cancellationToken = default);
    Task RemoveGradeLevelAsync(Guid teacherId, Guid gradeLevelId, CancellationToken cancellationToken = default);
}
