namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// Typed rejection for the file-store staging endpoint
/// (<c>POST /assignments/attachments/stage</c>, WS-A1 / FR-210). Carries
/// <see cref="IsSizeLimit"/> so the endpoint can map size-limit rejections
/// to <c>413 Payload Too Large</c> and the rest to <c>400</c> (decision
/// (d)). Thrown by <c>StagedFileValidator</c> BEFORE any file write —
/// the IFileStore receives zero calls on a rejection path. Mirrors the
/// typed-exception pattern from ar-1 / ar-3.
/// </summary>
public sealed class StagedFileRejectedException : Exception
{
    /// <summary>When true, the rejection is the per-file size cap and
    /// the endpoint should map it to <c>413</c>; otherwise <c>400</c>.</summary>
    public bool IsSizeLimit { get; }

    public StagedFileRejectedException(string message, bool isSizeLimit = false)
        : base(message)
    {
        IsSizeLimit = isSizeLimit;
    }
}
