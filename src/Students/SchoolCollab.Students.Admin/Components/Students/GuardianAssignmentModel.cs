using System.ComponentModel.DataAnnotations;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels;

namespace SchoolCollab.Students.Admin.Components.Students;

/// <summary>Form-state model for <see cref="GuardianPickerDialog"/>.</summary>
public sealed class GuardianAssignmentModel
{
    /// <summary>Set when linking an existing tenant guardian; null ⇒ create a new one.</summary>
    public Guid? ExistingGuardianId { get; set; }

    [Required] public string? FirstName { get; set; }
    [Required] public string? LastName { get; set; }

    public Guid? TitleCodedValueId { get; set; }
    public Guid? RelationshipCodedValueId { get; set; }
    public ContactChannel ContactChannel { get; set; } = ContactChannel.Email;
    public string? ContactValue { get; set; }

    /// <summary>True when editing an existing assignment (affects button labels).</summary>
    public bool IsEdit { get; set; }

    public static GuardianAssignmentModel ForAdd() => new();

    public static GuardianAssignmentModel ForEdit(GuardianAssignment a) => new()
    {
        ExistingGuardianId = a.ExistingGuardianId,
        FirstName = a.FirstName,
        LastName = a.LastName,
        TitleCodedValueId = a.TitleCodedValueId,
        RelationshipCodedValueId = a.RelationshipCodedValueId,
        ContactChannel = a.ContactChannel ?? ContactChannel.Email,
        ContactValue = a.ContactValue,
        IsEdit = true,
    };
}
