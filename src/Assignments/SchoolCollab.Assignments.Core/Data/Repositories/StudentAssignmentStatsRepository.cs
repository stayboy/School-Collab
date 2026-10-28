using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

/// <summary>
/// Issue #444 (layer L2a) — projects one <see cref="StudentAssignmentStatRow"/> per assignment the student
/// is a linked recipient of. The <c>Where</c> carries the recipient-link predicate ONLY: a <c>Draft</c> and
/// a not-yet-published <c>Scheduled</c> row must reach the fold, where the sent test lives (Q126, and the
/// row type's doc).
/// </summary>
internal sealed class StudentAssignmentStatsRepository(AssignmentsDbContext db) : IStudentAssignmentStatsRepository
{
    public Task<List<StudentAssignmentStatRow>> ListStudentAssignmentStatRowsAsync(Guid studentId, CancellationToken ct = default)
    {
        return db.Assignments.AsNoTracking()
            // Recipient link and nothing else — no status predicate. See the interface's doc: reusing the
            // ward read's visibility predicate here would make Assigned equal SentOut.
            .Where(a => db.AssignmentRecipients.Any(r => r.WardStudentId == studentId && r.AssignmentId == a.Id))
            .Select(a => new StudentAssignmentStatRow(
                a.Status,
                a.AvailableFromUtc,
                a.MandatoryReview,
                // The two submission flags come from the SAME row: (tenant, assignment, student) is unique
                // (uq_assignment_submissions_tenant_assignment_student), so the finalization and the review
                // state can never be read off two different submissions.
                db.AssignmentSubmissions.Any(s =>
                    s.AssignmentId == a.Id && s.StudentId == studentId && s.FinalizedAt != null),
                db.AssignmentSubmissions
                    .Where(s => s.AssignmentId == a.Id && s.StudentId == studentId)
                    .Select(s => (ReviewState?)s.ReviewState)
                    .FirstOrDefault(),
                db.GuardianSubmissionGates.Any(g =>
                    g.AssignmentId == a.Id && g.StudentId == studentId && g.ReviewedAt != null)))
            .ToListAsync(ct);
    }
}
