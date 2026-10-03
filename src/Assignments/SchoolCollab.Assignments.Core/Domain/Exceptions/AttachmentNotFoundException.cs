namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// R3 — thrown by the regenerate-extraction command when the assignment exists but carries no
/// attachment with the supplied id (a stale click in the Resources compartment, or an attachment
/// another tab already removed). Mapped to <c>404</c>, mirroring
/// <see cref="AssignmentNotFoundException"/>.
/// </summary>
public sealed class AttachmentNotFoundException(Guid attachmentId)
    : Exception($"Attachment '{attachmentId}' was not found on this assignment.")
{
    /// <summary>The attachment id that did not resolve.</summary>
    public Guid AttachmentId { get; } = attachmentId;
}
