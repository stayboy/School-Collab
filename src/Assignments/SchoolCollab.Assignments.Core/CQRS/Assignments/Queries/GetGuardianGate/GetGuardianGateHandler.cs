using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Assignments.Core.Data.Repositories;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetGuardianGate;

public sealed class GetGuardianGateHandler(
    ISubmissionRepository submissionRepository,
    ILogger<GetGuardianGateHandler> logger) : IQueryHandler<GetGuardianGate, GuardianGateDto?>
{
    public async Task<GuardianGateDto?> HandleAsync(GetGuardianGate query, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling GetGuardianGate for assignment {AssignmentId} / student {StudentId}",
            query.AssignmentId, query.StudentId);
        return await submissionRepository.GetGuardianGateAsync(query.AssignmentId, query.StudentId, cancellationToken);
    }
}
