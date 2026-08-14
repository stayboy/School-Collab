using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>
/// Form model for the assignment edit page. The DTO → form-model projection
/// (<see cref="From"/> / <see cref="LoadFrom"/>) lives on the model itself so
/// it is discoverable and unit-testable — see
/// documents/solution/dto-form-model-mapping.md.
/// </summary>
public sealed class AssignmentEditFormModel
{
    [System.ComponentModel.DataAnnotations.Required]
    public string? Title { get; set; }

    public string? Description { get; set; }

    public DateTime? DueDate { get; set; }

    public decimal? MaxScore { get; set; }

    /// <summary>
    /// Projects an <see cref="AssignmentSummaryDto"/> into a brand-new, fully-
    /// populated <see cref="AssignmentEditFormModel"/>.
    /// </summary>
    public static AssignmentEditFormModel From(AssignmentSummaryDto assignment)
    {
        var model = new AssignmentEditFormModel();
        model.LoadFrom(assignment);
        return model;
    }

    /// <summary>
    /// Loads this model's editable fields from an
    /// <see cref="AssignmentSummaryDto"/> in place. <see cref="DueDate"/>
    /// converts <see cref="DateTimeOffset"/> to <see cref="DateTime"/>.
    /// </summary>
    public void LoadFrom(AssignmentSummaryDto assignment)
    {
        Title = assignment.Title;
        Description = assignment.Description;
        DueDate = assignment.DueDate?.DateTime;
        MaxScore = assignment.MaxScore;
    }
}
