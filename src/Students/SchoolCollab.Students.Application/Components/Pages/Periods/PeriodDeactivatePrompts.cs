namespace SchoolCollab.Students.Application.Components.Pages.Periods;

/// <summary>
/// Single source of the Active-period deactivate + Deactivated-period archive
/// confirmation wording (period-edit-parity-deactivate.md FR-X9). Shared by the
/// Periods landing grid and the edit page so the wording is identical everywhere.
/// </summary>
public static class PeriodDeactivatePrompts
{
    public static string DeactivateMessage(string name) =>
        $"Deactivate \"{name}\"? It is no longer the active period and its date range " +
        "is freed so you can create a corrected period. Deactivated periods are kept " +
        "on record and can be archived later.";

    public static string ArchiveMessage(string name) =>
        $"Archive \"{name}\"? Its record is retired and kept for history.";
}
