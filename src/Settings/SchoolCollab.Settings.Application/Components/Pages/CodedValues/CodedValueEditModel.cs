using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Settings.Application.Components.Pages.CodedValues;

/// <summary>
/// Edit form model for the <c>/coded-values/{Code}/edit</c> page. Carries the
/// editable coded-value fields bound to the edit form. The DTO → form-model
/// projection (<see cref="From"/> / <see cref="LoadFrom"/>) lives on the model
/// itself so it is discoverable and unit-testable — see
/// documents/solution/dto-form-model-mapping.md.
/// </summary>
public sealed class CodedValueEditModel
{
    [System.ComponentModel.DataAnnotations.Required]
    public string? Name { get; set; }

    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>
    /// Projects a <see cref="CodedValueDto"/> into a brand-new, fully-populated
    /// <see cref="CodedValueEditModel"/>.
    /// </summary>
    public static CodedValueEditModel From(CodedValueDto codedValue)
    {
        var model = new CodedValueEditModel();
        model.LoadFrom(codedValue);
        return model;
    }

    /// <summary>
    /// Loads this model's fields from a <see cref="CodedValueDto"/> in place
    /// (for the common <c>readonly</c>-field case where the caller populates
    /// the model after an async load).
    /// </summary>
    public void LoadFrom(CodedValueDto codedValue)
    {
        Name = codedValue.Name;
        Description = codedValue.Description;
        DisplayOrder = codedValue.DisplayOrder;
    }
}
