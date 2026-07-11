namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Records how a student reached the next-period enrollment created when a period
/// closes (FR-A4): advanced one grade level (<see cref="Promoted"/>) or held back at
/// the same grade level (<see cref="Repeated"/>). Null for enrollments created
/// directly (not via the promotion carry-forward), so reporting can distinguish
/// who moved up vs. who repeated.
/// </summary>
public enum PromotionOutcome
{
    Promoted = 1,
    Repeated = 2
}
