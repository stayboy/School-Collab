namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>Teacher post-submission review state (spec §4.11 / §4.13).</summary>
public enum ReviewState
{
    Pending = 0,
    Reviewed = 1,
    Graded = 2
}
