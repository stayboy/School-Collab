using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

public interface IGuardianRepository
{
    Task<Guardian?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guardian?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Guardian guardian, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guardian guardian, CancellationToken cancellationToken = default);
    Task<GuardianNameHistory[]> GetNameHistoryAsync(Guid guardianId, CancellationToken cancellationToken = default);
    Task<StudentGuardian?> GetLinkAsync(Guid studentId, Guid guardianId, CancellationToken cancellationToken = default);
    Task AddLinkAsync(StudentGuardian link, CancellationToken cancellationToken = default);
    Task UpdateLinkAsync(StudentGuardian link, CancellationToken cancellationToken = default);
    Task RemoveLinkAsync(StudentGuardian link, CancellationToken cancellationToken = default);
    /// <summary>Persists any name-history rows held in-memory on <paramref name="guardian"/> that are not yet stored (the <see cref="Guardian.NameHistory"/> navigation is intentionally ignored by EF, so history is saved explicitly).</summary>
    Task PersistNameHistoryAsync(Guardian guardian, CancellationToken cancellationToken = default);
    Task<StudentGuardian[]> ListLinksByStudentAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<StudentGuardian[]> ListLinksByGuardianAsync(Guid guardianId, CancellationToken cancellationToken = default);
}
