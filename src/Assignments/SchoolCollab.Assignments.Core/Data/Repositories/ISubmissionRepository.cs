using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

/// <summary>
/// Persistence for the assignment submission lifecycle (spec §4.6–§4.13):
/// publish recipients, guardian submission gates, submissions + version history,
/// and teacher reviews. Queries project to the API contracts in
/// <see cref="SchoolCollab.Assignments.Contracts"/>.
/// </summary>
public interface ISubmissionRepository
{
    // Recipients
    Task<AssignmentRecipient?> GetRecipientAsync(Guid assignmentId, Guid contactId, CancellationToken ct = default);
    void Add(AssignmentRecipient recipient);
    void Update(AssignmentRecipient recipient);
    Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default);

    // Gates
    Task<GuardianSubmissionGate?> GetGateAsync(Guid gateId, CancellationToken ct = default);
    Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default);
    void Add(GuardianSubmissionGate gate);
    void Update(GuardianSubmissionGate gate);
    Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default);

    // Submissions
    Task<AssignmentSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken ct = default);
    Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default);
    void Add(AssignmentSubmission submission);
    void Update(AssignmentSubmission submission);

    // Versions + reviews
    void Add(AssignmentSubmissionVersion version);
    void Add(SubmissionReview review);

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    // Read models (Contracts DTOs)
    Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid teacherId, CancellationToken ct = default);
    Task<GuardianGateDto?> GetGuardianGateAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default);
}
