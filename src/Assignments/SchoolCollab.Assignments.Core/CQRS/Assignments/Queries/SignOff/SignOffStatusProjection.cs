using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;

/// <summary>
/// WS-C1 — shared builder for the per-ward sign-off status rows (spec §3.2 line
/// 51). Used by <see cref="ListSignOffStatusesQueryHandler"/> (teacher surface)
/// and the sign-off command handler (returns the refreshed row after a sign).
/// Names resolve via <see cref="IStudentDirectory"/> and degrade to the raw id
/// on a lookup miss (warn-logged once per round at the caller).
/// </summary>
internal static class SignOffStatusProjection
{
    public static async Task<IReadOnlyList<SignOffStatusDto>> BuildAsync(
        Guid assignmentId,
        IAssignmentRepository assignmentRepository,
        ISubmissionRepository submissionRepository,
        IStudentDirectory studentDirectory,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        // The caller guards RequiresSignature (empty rows for non-signature
        // assignments are the list handler's defensive posture); the assignment
        // fetch doubles as the 404 guard there.
        var submissions = await submissionRepository.ListSubmissionEntitiesByAssignmentAsync(assignmentId, cancellationToken);
        var recipients = await submissionRepository.ListRecipientEntitiesByAssignmentAsync(assignmentId, cancellationToken);

        var versionRows = submissions.Count == 0
            ? []
            : await submissionRepository.ListVersionsForSubmissionIdsAsync(
                submissions.Select(s => s.Id).ToList(), cancellationToken);

        var rows = new List<SignOffStatusDto>(submissions.Count);
        foreach (var submission in submissions)
        {
            // The signer is authoritative on the audit event (we do not store the
            // signer id on the submission).
            var eventRow = await submissionRepository.GetSignatureEventByAssignmentStudentAsync(
                submission.AssignmentId, submission.StudentId, cancellationToken);

            var studentTask = studentDirectory.GetStudentNameAsync(submission.StudentId, cancellationToken);
            var guardiansTask = studentDirectory.GetGuardiansAsync(submission.StudentId, cancellationToken);
            await Task.WhenAll(studentTask, guardiansTask);
            var studentName = studentTask.Result;
            var guardians = guardiansTask.Result;

            var signerInfo = eventRow is not null
                ? guardians.FirstOrDefault(g => g.GuardianId == eventRow.SignerGuardianId)
                : null;
            var expectedInfo = submission.ExpectedSignerGuardianId is Guid expected
                ? guardians.FirstOrDefault(g => g.GuardianId == expected)
                : null;

            var wardRecipients = recipients.Where(r => r.WardStudentId == submission.StudentId).ToList();

            var currentVersion = versionRows
                .Where(v => v.SubmissionId == submission.Id && v.VersionNumber == submission.CurrentVersionNumber)
                .FirstOrDefault();

            if (eventRow is null && studentName is null && expectedInfo is null)
            {
                logger.LogWarning(
                    "Sign-off projection for assignment {AssignmentId} / student {StudentId} degraded a name lookup",
                    assignmentId, submission.StudentId);
            }
            rows.Add(new SignOffStatusDto(
                submission.StudentId,
                studentName?.DisplayName ?? submission.StudentId.ToString(),
                submission.ExpectedSignerGuardianId,
                expectedInfo?.DisplayName,
                eventRow?.SignerGuardianId,
                signerInfo?.DisplayName ?? eventRow?.SignerGuardianId.ToString(),
                (SignOffStateDto)(int)submission.SignOffState,
                submission.SignedAt,
                submission.FinalizedAt,
                Delivered: wardRecipients.Any(r => r.DeliveredAt is not null),
                Opened: wardRecipients.Any(r => r.OpenedAt is not null),
                submission.CurrentVersionNumber,
                currentVersion?.Score,
                currentVersion?.Passed));
        }

        logger.LogInformation("Listed {Count} sign-off rows for assignment {AssignmentId}", rows.Count, assignmentId);
        return rows;
    }
}