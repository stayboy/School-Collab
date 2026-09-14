using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;

/// <summary>
/// WS-C2 — assembles the <see cref="SignOffContextDto"/> aggregate for the
/// guardian sign page (spec §3.2). Guards: assignment exists (404), the
/// assignment requires a signature, and a submission exists for the pair. Names
/// degrade to the raw id when the directory lookup misses.
/// </summary>
public sealed class GetSignOffContextQueryHandler(
    IAssignmentRepository assignmentRepository,
    ISubmissionRepository submissionRepository,
    IStudentDirectory studentDirectory,
    ISignatureConsentTextResolver consentResolver,
    ILogger<GetSignOffContextQueryHandler> logger) : IQueryHandler<GetSignOffContextQuery, SignOffContextDto>
{
    public async Task<SignOffContextDto> HandleAsync(
        GetSignOffContextQuery query, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting sign-off context for assignment {AssignmentId} / student {StudentId}",
            query.AssignmentId, query.StudentId);

        var assignment = await assignmentRepository.GetAsync(query.AssignmentId, cancellationToken)
            ?? throw new AssignmentNotFoundException(query.AssignmentId);

        if (!assignment.RequiresSignature)
        {
            throw new SubmissionSignOffStateException(
                "This assignment does not require a guardian signature.");
        }

        var submission = await submissionRepository.GetSubmissionByAssignmentStudentAsync(
            query.AssignmentId, query.StudentId, cancellationToken)
            ?? throw new SubmissionNotFoundException(query.StudentId);

        var studentName = (await studentDirectory.GetStudentNameAsync(query.StudentId, cancellationToken))
            ?.DisplayName ?? query.StudentId.ToString();

        var guardians = await studentDirectory.GetGuardiansAsync(query.StudentId, cancellationToken);

        var consentText = await consentResolver.ResolveConsentTextAsync(cancellationToken);

        // Review summary fields from the current version (the ar-6 projection).
        AssignmentSubmissionVersion? currentVersion = null;
        if (submission.CurrentVersionNumber > 0)
        {
            var versions = await submissionRepository.ListVersionsForSubmissionIdsAsync(
                [submission.Id], cancellationToken);
            currentVersion = versions.FirstOrDefault(v => v.VersionNumber == submission.CurrentVersionNumber);
        }

        return new SignOffContextDto(
            assignment.Id,
            submission.StudentId,
            assignment.Title,
            studentName,
            (SignOffStateDto)(int)submission.SignOffState,
            submission.SignedAt,
            submission.FinalizedAt,
            submission.CurrentVersionNumber,
            currentVersion?.Score,
            currentVersion?.Passed,
            consentText,
            guardians
                .Select(g => new WardGuardianDto(g.GuardianId, g.DisplayName, g.IsPrimary))
                .ToList());
    }
}
