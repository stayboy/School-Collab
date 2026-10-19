using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;

/// <summary>
/// Publishes an assignment (spec §8) and queues the recipient notifications.
/// <para><see cref="ContactIds"/> non-empty ⇒ only those subscribed contacts are reached;
/// null/empty ⇒ all subscribed contacts.</para>
/// <para>Round notification-templates-b2a: <see cref="Channels"/> is the per-sendout medium
/// restriction (§3.8) and the two stamps are the author's template selection (§3.7). The stamps
/// are <b>reuse-on-null</b>: a null value keeps the assignment's stored selection, and the columns
/// are cleared only by an explicit clear operation (B2b's dialog; B2a resets them directly in
/// tests).</para>
/// </summary>
public sealed record PublishAssignmentCommand(
    Guid Id,
    IReadOnlyList<Guid>? ContactIds = null,
    PublishChannel? Channels = null,
    Guid? PublishEmailTemplateId = null,
    Guid? PublishSmsTemplateId = null) : ICommand;
