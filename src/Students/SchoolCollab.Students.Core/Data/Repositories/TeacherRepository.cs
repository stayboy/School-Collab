using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class TeacherRepository(StudentsDbContext db)
    : SoftDeletableRepositoryBase<Teacher, StudentsDbContext>(db), ITeacherRepository
{
    public Task AddSubjectAsync(TeacherSubject link, CancellationToken cancellationToken = default)
    {
        Db.TeacherSubjects.Add(link);
        return Db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveSubjectAsync(Guid teacherId, Guid subjectId, CancellationToken cancellationToken = default)
    {
        var link = await Db.TeacherSubjects
            .FirstOrDefaultAsync(l => l.TeacherId == teacherId && l.SubjectId == subjectId, cancellationToken);
        if (link is not null)
        {
            Db.TeacherSubjects.Remove(link);
            await Db.SaveChangesAsync(cancellationToken);
        }
    }

    public Task AddGradeLevelAsync(TeacherGradeLevel link, CancellationToken cancellationToken = default)
    {
        Db.TeacherGradeLevels.Add(link);
        return Db.SaveChangesAsync(cancellationToken);
    }

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
