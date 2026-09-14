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

    // WS-C1 sign-off (spec §5 / §6 NFR line 115): audit-event persistence + raw
    // entity reads for the sign-off query handlers.
    public void Add(SignatureEvent signatureEvent) => db.SignatureEvents.Add(signatureEvent);

    public Task<SignatureEvent?> GetSignatureEventByAssignmentStudentAsync(
        Guid assignmentId, Guid studentId, CancellationToken ct = default) =>
        db.SignatureEvents.FirstOrDefaultAsync(
            e => e.AssignmentId == assignmentId && e.StudentId == studentId, ct);

    public Task<List<AssignmentSubmission>> ListSubmissionEntitiesByAssignmentAsync(
        Guid assignmentId, CancellationToken ct = default) =>
        db.AssignmentSubmissions
            .Where(s => s.AssignmentId == assignmentId)
            .OrderBy(s => s.StudentId)
            .ToListAsync(ct);

    public Task<List<AssignmentRecipient>> ListRecipientEntitiesByAssignmentAsync(
        Guid assignmentId, CancellationToken ct = default) =>
        db.AssignmentRecipients
            .Where(r => r.AssignmentId == assignmentId)
            .ToListAsync(ct);

    public Task<List<AssignmentSubmissionVersion>> ListVersionsForSubmissionIdsAsync(
        IReadOnlyList<Guid> submissionIds, CancellationToken ct = default)
    {
        if (submissionIds.Count == 0)
        {
            return Task.FromResult(new List<AssignmentSubmissionVersion>());
        }
        return db.AssignmentSubmissionVersions
            .Where(v => submissionIds.Contains(v.SubmissionId))
            .ToListAsync(ct);
    }

    public void Add(AssignmentSubmissionVersion version) => db.AssignmentSubmissionVersions.Add(version);
    public void Add(SubmissionReview review) => db.SubmissionReviews.Add(review);
    // WS-A3 (spec §3.3): append a structured answer row to the DbContext
    // change tracker. The handler scores before creating the version,
    // then iterates the inbound answers and adds one row per (version,
    // question) pair — per-version analytics read pattern.
    public void Add(SubmissionAnswer answer) => db.SubmissionAnswers.Add(answer);

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

    public async Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var query = from s in db.AssignmentSubmissions
                    join a in db.Assignments on s.AssignmentId equals a.Id
                    where s.AssignmentId == assignmentId
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

    public async Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var query = from r in db.AssignmentRecipients
                    where r.AssignmentId == assignmentId
                    orderby r.OwnerType, r.OwnerId
                    select new AssignmentRecipientDto(
                        r.Id,
                        r.AssignmentId,
                        (ContactOwnerTypeDto)(int)r.OwnerType,
                        r.OwnerId,
                        r.WardStudentId,
                        r.ContactId,
                        (ContactChannelDto)(int)r.Channel,
                        r.Role == null ? (GuardianRoleDto?)null : (GuardianRoleDto)(int)r.Role,
                        r.NotifyOnBroadcast,
                        r.SubscriptionActive);

        return await query.ToArrayAsync(ct);
    }

    public async Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
    {
        var submission = await db.AssignmentSubmissions
            .FirstOrDefaultAsync(s => s.AssignmentId == assignmentId && s.StudentId == studentId, ct);
        if (submission is null)
            return null;

        var versions = await (from v in db.AssignmentSubmissionVersions
                               where v.SubmissionId == submission.Id
                               orderby v.VersionNumber
                               select new SubmissionVersionDto(
                                   v.Id,
                                   v.VersionNumber,
                                   (SubmissionSourceDto)(int)v.Source,
                                   v.Content,
                                   v.SubmittedByGuardianId,
                                   v.SubmittedAt,
                                   // WS-A3 (spec §3.3): auto-scored total + pass
                                   // flag persisted per version. Both nullable
                                   // — TeacherGraded submissions leave them
                                   // null, and a missing PassScore on the
                                   // assignment leaves Passed null.
                                   v.Score,
                                   v.Passed)).ToArrayAsync(ct);

        var review = await (from r in db.SubmissionReviews
                            where r.SubmissionId == submission.Id
                            orderby r.CreatedAt descending
                            select new SubmissionReviewDto(
                                r.Id,
                                r.SubmissionId,
                                r.TeacherId,
                                r.Score,
                                r.Grade,
                                r.Comments,
                                r.CreatedAt)).FirstOrDefaultAsync(ct);

        return new SubmissionDetailDto(
            submission.Id,
            submission.AssignmentId,
            submission.StudentId,
            submission.CurrentVersionNumber,
            (ReviewStateDto)(int)submission.ReviewState,
            submission.LastSubmittedAt,
            versions,
            review,
            // WS-C1 (spec §3.2 line 51): the four sign-off fields ride on the
            // submission-detail DTO (named-arg trailing position).
            (SignOffStateDto)(int)submission.SignOffState,
            submission.SignedAt,
            submission.FinalizedAt,
            submission.ExpectedSignerGuardianId);
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
