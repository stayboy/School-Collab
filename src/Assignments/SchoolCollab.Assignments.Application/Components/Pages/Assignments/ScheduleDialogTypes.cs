using System.ComponentModel.DataAnnotations;

namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>Form state for the assignment schedule dialog (WS-A2 /
/// spec §3.5 step 2). The submit lands at 00:00 UTC of the chosen
/// date (the wizard DueDate precedent).</summary>
public sealed class ScheduleFormModel
{
    /// <summary>The chosen calendar date. Required \u2014 the dialog
    /// surfaces an inline error when the field is blank.</summary>
    [Required]
    public DateTime? AvailableFrom { get; set; }
}

/// <summary>Result returned when the teacher confirms schedule
/// (spec §3.5 step 2). <see cref="AvailableFromUtc"/> is always
/// 00:00 UTC of the chosen date so the sweep tick has a deterministic
/// moment to compare against.</summary>
public sealed record ScheduleResult(DateTimeOffset AvailableFromUtc);
