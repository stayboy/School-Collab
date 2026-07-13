using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

internal sealed class SubmissionRepository(AssignmentsDbContext db) : ISubmissionRepository
{
    public Task<AssignmentRecipient?> GetRecipientAsync(Guid assignmentId, Guid contactId, CancellationToken ct = default) =>
        db.AssignmentRecipients.FirstOrDefaultAsync(r => r.AssignmentId == assignmentId && r.ContactId == contactId, ct);

    public void Add(AssignmentRecipient recipient) => db.AssignmentRecipients.Add(recipient);
    public void Update(AssignmentRecipient recipient) => db.AssignmentRecipients.Update(recipient);

    public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) =>
        db.AssignmentRecipients.Where(r => r.AssignmentId == assignmentId).ExecuteDeleteAsync(ct);

    public Task<GuardianSubmissionGate?> GetGateAsync(Guid gateId, CancellationToken ct = default) =>
        db.GuardianSubmissionGates.FirstOrDefaultAsync(g => g.Id == gateId, ct);

    public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default) =>
        db.GuardianSubmissionGates.FirstOrDefaultAsync(g => g.AssignmentId == assignmentId && g.StudentId == studentId, ct);

    public void Add(GuardianSubmissionGate gate) => db.GuardianSubmissionGates.Add(gate);
    public void Update(GuardianSubmissionGate gate) => db.GuardianSubmissionGates.Update(gate);

    public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) =>
        db.GuardianSubmissionGates.Where(g => g.AssignmentId == assignmentId).ToListAsync(ct);

    public Task<AssignmentSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken ct = default) =>
        db.AssignmentSubmissions.FirstOrDefaultAsync(s => s.Id == submissionId, ct);

    public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default) =>
        db.AssignmentSubmissions.FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.StudentId == studentId, ct);

    public void Add(AssignmentSubmission submission) => db.AssignmentSubmissions.Add(submission);
    public void Update(AssignmentSubmission submission) => db.AssignmentSubmissions.Update(submission);

    public void Add(AssignmentSubmissionVersion version) => db.AssignmentSubmissionVersions.Add(version);
    public void Add(SubmissionReview review) => db.SubmissionReviews.Add(review);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid teacherId, CancellationToken ct = default)
    {
        var query = from s in db.AssignmentSubmissions
                    join a in db.Assignments on s.AssignmentId equals a.Id
                    where a.CreatedByTeacherId == teacherId
                    orderby s.LastSubmittedAt descending
                    select new SubmissionForReviewDto(
                        s.Id,
                        a.Id,
                        a.Title,
                        s.StudentId,
                        s.CurrentVersionNumber,
                        (ReviewStateDto)(int)s.ReviewState,
                        s.LastSubmittedAt);

        return await query.ToArrayAsync(ct);
    }

    public async Task<GuardianGateDto?> GetGuardianGateAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
    {
        var gate = await db.GuardianSubmissionGates
            .FirstOrDefaultAsync(g => g.AssignmentId == assignmentId && g.StudentId == studentId, ct);

        return gate is null
            ? null
            : new GuardianGateDto(
                gate.Id,
                gate.AssignmentId,
                gate.StudentId,
                gate.SubmissionEnabledForStudent,
                gate.ReviewedAt,
                gate.ReviewedByGuardianId,
                gate.ReviewComment,
                gate.SubmittedByGuardianId,
                gate.SubmittedByGuardianAt);
    }
}
