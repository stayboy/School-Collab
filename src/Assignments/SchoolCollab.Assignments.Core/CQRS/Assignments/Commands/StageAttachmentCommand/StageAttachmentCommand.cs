using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.StageAttachmentCommand;

/// <summary>Stages one uploaded file via <see cref="IFileStore"/> (WS-A1 /
/// FR-210 / EC-4). The wizard's Resources UI calls this at selection time
/// (decision (b) — stage-at-selection) and rides the returned
/// <see cref="StagedAttachmentDto.StoragePath"/> on the create payload.
/// Scrutor auto-registers the handler from the assembly scan.</summary>
public sealed record StageAttachmentCommand(
    Stream Content,
    string FileName,
    string ContentType,
    long FileSize) : ICommand;
