namespace SchoolCollab.Students.Application.Components.Pages.Periods;

/// <summary>
/// Single source of the Draft-period delete confirmation wording shared by the
/// Periods landing grid, the edit page danger zone, and SubPeriodsSection rows
/// (period-draft-delete.md FR-D9/D10/D12 — "same confirmation wording").
/// </summary>
public static class PeriodDeletePrompts
{
    public static string YearMessage(string name, int draftSubPeriodCount) =>
        $"Delete \"{name}\"? This permanently deletes the academic year and its " +
        $"{draftSubPeriodCount} draft sub-period{(draftSubPeriodCount == 1 ? "" : "s")} " +
        "that go with it. This cannot be undone.";

    public static string SubPeriodMessage(string name, string kindLabel) =>
        $"Delete \"{name}\"? This permanently deletes this {kindLabel}. " +
        "This cannot be undone.";
}
