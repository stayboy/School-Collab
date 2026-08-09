namespace SchoolCollab.Admin.Shared.Components.Dialogs;

/// <summary>
/// Content for the reusable <see cref="ConfirmDialog"/> — a modal confirmation
/// prompt with a warning message and Primary / Secondary buttons. Shown via
/// <see cref="DialogServiceExtensions.ShowConfirmDialogAsync"/>.
/// </summary>
public sealed record ConfirmDialogContent(
    string Message,
    string PrimaryText,
    string SecondaryText = "Cancel",
    string? Title = null);
