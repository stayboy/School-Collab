namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// Round notification-templates-b2a (spec §3.7 / WS5) — thrown when a publish request carries an
/// <b>explicit</b> template stamp that is not the live Publish template for its channel in the
/// caller's tenant (a deleted template, another tenant's id, or the wrong channel's id). Typed
/// (never <see cref="InvalidOperationException"/>) and mapped to <c>400</c> by the publish route:
/// an explicit selection is author intent, so it fails loudly rather than silently falling back.
/// A <b>stored</b> stamp that no longer validates never throws — §3.7 pins that it degrades and
/// never blocks a publish.
/// </summary>
public sealed class NotificationTemplateSelectionInvalidException(string message) : Exception(message);
