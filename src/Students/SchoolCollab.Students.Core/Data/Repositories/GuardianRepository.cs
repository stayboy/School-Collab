using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class GuardianRepository(StudentsDbContext db)
    : SoftDeletableRepositoryBase<Guardian, StudentsDbContext>(db), IGuardianRepository
{
    public Task<GuardianNameHistory[]> GetNameHistoryAsync(Guid guardianId, CancellationToken cancellationToken = default) =>
        Db.GuardianNameHistories
            .AsNoTracking()
            .Where(h => h.GuardianId == guardianId)
            .OrderBy(h => h.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public Task<StudentGuardian?> GetLinkAsync(Guid studentId, Guid guardianId, CancellationToken cancellationToken = default) =>
        Db.StudentGuardians
            .FirstOrDefaultAsync(l => l.StudentId == studentId && l.GuardianId == guardianId, cancellationToken);

    public Task AddLinkAsync(StudentGuardian link, CancellationToken cancellationToken = default)
    {
        Db.StudentGuardians.Add(link);
        return Db.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateLinkAsync(StudentGuardian link, CancellationToken cancellationToken = default) =>
        Db.SaveChangesAsync(cancellationToken);

    public Task RemoveLinkAsync(StudentGuardian link, CancellationToken cancellationToken = default)
    {
        Db.StudentGuardians.Remove(link);
        return Db.SaveChangesAsync(cancellationToken);
    }

    public async Task PersistNameHistoryAsync(Guardian guardian, CancellationToken cancellationToken = default)
    {
        var existingIds = await Db.GuardianNameHistories
            .Where(h => h.GuardianId == guardian.Id)
            .Select(h => h.Id)
            .ToArrayAsync(cancellationToken);
        var known = new HashSet<Guid>(existingIds);
        foreach (var h in guardian.NameHistory)
        {
            if (!known.Contains(h.Id))
            {
                Db.GuardianNameHistories.Add(h);
            }
        }

        if (Db.ChangeTracker.HasChanges())
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
    }

    public Task<StudentGuardian[]> ListLinksByStudentAsync(Guid studentId, CancellationToken cancellationToken = default) =>
        Db.StudentGuardians
            .AsNoTracking()
            .Where(l => l.StudentId == studentId)
            .OrderBy(l => l.Role)
            .ToArrayAsync(cancellationToken);

    public Task<StudentGuardian[]> ListLinksByGuardianAsync(Guid guardianId, CancellationToken cancellationToken = default) =>
        Db.StudentGuardians
            .AsNoTracking()
            .Where(l => l.GuardianId == guardianId)
            .ToArrayAsync(cancellationToken);
}
