namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RegenerateAttachmentExtraction;

using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;

/// <summary>
/// R3 (D3/D4, criterion 5) — re-reads one persisted attachment's staged bytes and replaces its
/// stored extraction. This is the <b>only</b> path that rewrites an extraction after the initial
/// stage (D3: regeneration is user-triggered); a later generate never silently re-extracts.
/// </summary>
public sealed record RegenerateAttachmentExtractionCommand(Guid AssignmentId, Guid AttachmentId)
    : ICommand;
