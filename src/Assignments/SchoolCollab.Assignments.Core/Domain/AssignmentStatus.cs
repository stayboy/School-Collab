namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>Lifecycle states of an assignment (spec §3.5 / WS-A2).
/// Enum ints are persisted — adding members at the END is additive
/// (the migration adds the new state columns separately).</summary>
public enum AssignmentStatus
{
    Draft = 0,
    Published = 1,
    Closed = 2,
    /// <summary>The assignment is scheduled to auto-publish at
    /// <see cref="Assignment.AvailableFromUtc"/>. Transitions to
    /// <see cref="Published"/> when that moment arrives (spec §3.5 step 8).</summary>
    Scheduled = 3,
    /// <summary>The assignment is archived (read-only retention —
    /// spec §7 Q6). The transition is driven by the archive sweep after
    /// the due date + <see cref="Assignment.ArchiveGraceDays"/> window.</summary>
    Archived = 4
}
