using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Admin.Shared.Components;

/// <summary>Working-copy model for <see cref="GuardianEditFields"/>. Holds
/// the guardian's link-metadata fields (title / names / relationship / role)
/// as a single reference-type object so the host binds the component with
/// one <c>Model</c> parameter instead of five two-way primitive bindings.
/// The component mutates <c>Model.FirstName</c> etc. via <c>@bind-Value</c>
/// on the inputs, and the host reads the accumulated values on Save
/// (<c>SaveAddGuardianAsync</c> / <c>SaveEditGuardianAsync</c> in
/// <c>GuardianSection</c>). Mirrors the existing pattern used by
/// <c>GuardianFormFields</c> with its <c>GuardianAssignmentModel</c>.</summary>
public sealed class GuardianEditFieldsModel
{
    /// <summary>The guardian's title / salutation coded-value id.</summary>
    public Guid? TitleCodedValueId { get; set; }

    /// <summary>The guardian's first name.</summary>
    public string? FirstName { get; set; }

    /// <summary>The guardian's last name.</summary>
    public string? LastName { get; set; }

    /// <summary>The link relationship coded-value id.</summary>
    public Guid? RelationshipCodedValueId { get; set; }

    /// <summary>The guardian's role on the student (Primary / CC). Defaults
    /// to <see cref="GuardianRole.Primary"/>.</summary>
    public GuardianRole Role { get; set; } = GuardianRole.Primary;

    /// <summary>Convenience property for <c>FluentCheckbox</c> binding.
    /// Checked = <see cref="GuardianRole.CC"/>, unchecked = <see
    /// cref="GuardianRole.Primary"/> (default polarity, reversible).</summary>
    public bool IsCC
    {
        get => Role == GuardianRole.CC;
        set => Role = value ? GuardianRole.CC : GuardianRole.Primary;
    }
}
