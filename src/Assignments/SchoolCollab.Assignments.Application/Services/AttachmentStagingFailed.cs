using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Services;

/// <summary>
/// Typed exception thrown by <see cref="AssignmentsApiClient.StageAttachmentAsync"/>
/// when the staging endpoint returns <c>400</c> or <c>413</c> (WS-A1 /
/// FR-210). Mirrors <see cref="QuestionGenerationFailed"/> exactly —
/// spec-blessed name without the <c>Exception</c> suffix; the repo's
/// typed-exception rule is satisfied by <em>typedness</em>, not by
/// the suffix (see that file's own doc note). The message is the
/// server-supplied one (e.g. <c>"'.exe' files are not allowed."</c>) so
/// the wizard can render it directly in the Resources section error bar.
/// </summary>
public sealed class AttachmentStagingFailed : Exception
{
    /// <summary>The most recent staging attempt's returned
    /// <see cref="StagedAttachmentDto"/>, if any. Null when the request
    /// was rejected by the server (no storage path was issued).</summary>
    public StagedAttachmentDto? Result { get; }

    public AttachmentStagingFailed(string message) : base(message) { }
}
