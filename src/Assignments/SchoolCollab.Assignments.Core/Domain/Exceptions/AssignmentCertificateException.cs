namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// C3 — raised when the sign-off certificate cannot be generated or stored
/// during finalize (mirrors the <see cref="AssignmentApprovalRequiredException"/>
/// typed-exception posture). The finalize route maps it to HTTP 502 (the ar-2
/// provider-failure precedent) so a downstream renderer/IFileStore failure is
/// distinguishable from the assignment 500 default. Wrapping keeps the failure
/// typed: callers retry by finalizing again (the finalize is transactional —
/// a generation failure rolls back the finalize without persisting it).
/// </summary>
public sealed class AssignmentCertificateException : Exception
{
    public AssignmentCertificateException(string message) : base(message) { }

    public AssignmentCertificateException(string message, Exception innerException)
        : base(message, innerException) { }
}
