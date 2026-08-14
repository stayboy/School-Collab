using System.ComponentModel.DataAnnotations;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>
/// Form model for the grade-level create / edit form (bound by
/// <c>GradeLevelFormFields.razor</c>). Extracted from the component's
/// <c>@code</c> block into a standalone class so the DTO → form-model
/// projection (<see cref="From"/> / <see cref="LoadFrom"/>) can live on the
/// model itself and be unit-tested — see
/// documents/solution/dto-form-model-mapping.md.
/// </summary>
public sealed class GradeLevelFormModel
{
    [Required]
    public Guid? CodedValueId { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string? Name { get; set; }

    public int Level { get; set; }
    public int DisplayOrder { get; set; }

    // Enrollment validation guard clauses (plan §2/§10).
    public int? MinAge { get; set; }
    public int? MaxAge { get; set; }
    public Guid? AllowedGenderCodedValueId { get; set; }

    /// <summary>
    /// Projects a <see cref="GradeLevelDto"/> into a brand-new, fully-populated
    /// <see cref="GradeLevelFormModel"/>. Use when the caller owns the model
    /// (tests, or a page that swaps the whole model rather than mutating one).
    /// </summary>
    public static GradeLevelFormModel From(GradeLevelDto grade)
    {
        var model = new GradeLevelFormModel();
        model.LoadFrom(grade);
        return model;
    }

    /// <summary>
    /// Loads this model's fields from a <see cref="GradeLevelDto"/> in place.
    /// Use when the caller holds a <c>readonly</c> model instance it must
    /// populate after the async load (the edit page keeps <c>_model</c> as a
    /// <c>readonly</c> field and mutates it after fetching the grade).
    /// </summary>
    public void LoadFrom(GradeLevelDto grade)
    {
        CodedValueId = grade.CodedValueId;
        Name = grade.Name;
        Level = grade.Level;
        DisplayOrder = grade.DisplayOrder;
        MinAge = grade.MinAge;
        MaxAge = grade.MaxAge;
        AllowedGenderCodedValueId = grade.AllowedGenderCodedValueId;
    }
}