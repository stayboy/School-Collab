namespace SchoolCollab.Students.Core.Domain;

public enum PeriodStatus
{
    Draft = 0,
    Active = 1,
    Completed = 2,
    Archived = 3,

    // period-edit-parity-deactivate.md FR-X1: set by Deactivate() from Active;
    // excluded from the no-overlap check (FR-X3) so a corrected period can be
    // created in its freed range. Not deletable (Draft-only delete).
    Deactivated = 4
}