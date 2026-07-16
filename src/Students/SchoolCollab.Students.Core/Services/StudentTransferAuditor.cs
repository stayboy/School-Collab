using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Services;

/// <summary>
/// Writes a <see cref="StudentTransferAuditEntry"/> into the supplied
/// <see cref="StudentsDbContext"/> so the audit row is persisted in the same
/// transaction as the enrollment transfer (the handler calls
/// <see cref="StudentsDbContext.SaveChangesAsync"/> via the repository after
/// both the mutation and the audit row are tracked). Append-only: never updates
/// or deletes.
/// </summary>
public sealed class StudentTransferAuditor(IActorAccessor actorAccessor)
{
    public void Record(
        StudentsDbContext db,
        Guid tenantId,
        Guid studentId,
        Guid fromGradeLevelId,
        Guid toGradeLevelId,
        Guid periodId,
        string? reason)
    {
        db.StudentTransferAuditEntries.Add(StudentTransferAuditEntry.Create(
            tenantId: tenantId,
            studentId: studentId,
            fromGradeLevelId: fromGradeLevelId,
            toGradeLevelId: toGradeLevelId,
            periodId: periodId,
            reason: reason,
            actorId: actorAccessor.ActorId,
            actorDisplayName: actorAccessor.ActorDisplayName));
    }
}
