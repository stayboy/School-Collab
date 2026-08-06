using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class TeacherRepository(StudentsDbContext db)
    : SoftDeletableRepositoryBase<Teacher, StudentsDbContext>(db), ITeacherRepository
{
    public Task AddTopicAsync(TeacherTopic link, CancellationToken cancellationToken = default)
    {
        Db.TeacherTopics.Add(link);
        return Db.SaveChangesAsync(cancellationToken);
    }

    public Task<TeacherTopic?> GetTopicLinkAsync(Guid teacherId, Guid topicId, CancellationToken cancellationToken = default)
        => Db.TeacherTopics.FirstOrDefaultAsync(l => l.TeacherId == teacherId && l.TopicId == topicId, cancellationToken);

    public async Task RemoveTopicAsync(Guid teacherId, Guid topicId, CancellationToken cancellationToken = default)
    {
        var link = await Db.TeacherTopics
            .FirstOrDefaultAsync(l => l.TeacherId == teacherId && l.TopicId == topicId, cancellationToken);
        if (link is not null)
        {
            Db.TeacherTopics.Remove(link);
            await Db.SaveChangesAsync(cancellationToken);
        }
    }

    public Task UpdateTopicAsync(TeacherTopic link, CancellationToken cancellationToken = default)
    {
        Db.TeacherTopics.Update(link);
        return Db.SaveChangesAsync(cancellationToken);
    }

    public Task AddQualificationAsync(TeacherQualification link, CancellationToken cancellationToken = default)
    {
        Db.TeacherQualifications.Add(link);
        return Db.SaveChangesAsync(cancellationToken);
    }

    public Task<Guid[]> GetQualificationCodedValueIdsAsync(Guid teacherId, CancellationToken cancellationToken = default)
        => Db.TeacherQualifications
            .Where(q => q.TeacherId == teacherId)
            .Select(q => q.CodedValueId)
            .ToArrayAsync(cancellationToken);

    public async Task RemoveQualificationAsync(Guid teacherId, Guid codedValueId, CancellationToken cancellationToken = default)
    {
        var link = await Db.TeacherQualifications
            .FirstOrDefaultAsync(q => q.TeacherId == teacherId && q.CodedValueId == codedValueId, cancellationToken);
        if (link is not null)
        {
            Db.TeacherQualifications.Remove(link);
            await Db.SaveChangesAsync(cancellationToken);
        }
    }

    public Task AddGradeLevelAsync(TeacherGradeLevel link, CancellationToken cancellationToken = default)
    {
        Db.TeacherGradeLevels.Add(link);
        return Db.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateGradeLevelAsync(TeacherGradeLevel link, CancellationToken cancellationToken = default)
    {
        Db.TeacherGradeLevels.Update(link);
        return Db.SaveChangesAsync(cancellationToken);
    }

    public Task<TeacherGradeLevel?> GetGradeLevelLinkAsync(Guid teacherId, Guid gradeLevelId, CancellationToken cancellationToken = default)
        => Db.TeacherGradeLevels.FirstOrDefaultAsync(l => l.TeacherId == teacherId && l.GradeLevelId == gradeLevelId, cancellationToken);

    public async Task RemoveGradeLevelAsync(Guid teacherId, Guid gradeLevelId, CancellationToken cancellationToken = default)
    {
        var link = await Db.TeacherGradeLevels
            .FirstOrDefaultAsync(l => l.TeacherId == teacherId && l.GradeLevelId == gradeLevelId, cancellationToken);
        if (link is not null)
        {
            Db.TeacherGradeLevels.Remove(link);
            await Db.SaveChangesAsync(cancellationToken);
        }
    }

    public Task SoftDeleteAsync(Teacher teacher, CancellationToken cancellationToken = default)
    {
        teacher.SoftDelete();
        return UpdateAsync(teacher, cancellationToken);
    }
}
