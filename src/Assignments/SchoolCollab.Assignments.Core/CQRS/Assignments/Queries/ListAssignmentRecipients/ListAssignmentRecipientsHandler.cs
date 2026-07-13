using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data.Repositories;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentRecipients;

public sealed class ListAssignmentRecipientsHandler(
    ISubmissionRepository submissionRepository,
    ILogger<ListAssignmentRecipientsHandler> logger) : IQueryHandler<ListAssignmentRecipients, AssignmentRecipientDto[]>
{
    public async Task<AssignmentRecipientDto[]> HandleAsync(ListAssignmentRecipients query, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling ListAssignmentRecipients for assignment {AssignmentId}", query.AssignmentId);
        return await submissionRepository.ListRecipientsForAssignmentAsync(query.AssignmentId, cancellationToken);
    }
}