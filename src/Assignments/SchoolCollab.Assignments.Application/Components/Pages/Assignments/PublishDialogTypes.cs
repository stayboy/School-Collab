using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>Form state for the assignment publish dialog (spec §12).</summary>
public sealed record PublishFormModel(
    IReadOnlyList<SubscribedContactDto> Contacts,
    string AudienceSummary,
    bool MandatoryReview)
{
    public bool LimitSelection { get; set; }

    /// <summary>Mutable per-contact selection (FluentCheckbox binds to IsSelected).</summary>
    public List<ContactSelection> Selections { get; set; } = new();
}

/// <summary>One subscribed contact plus its checkbox state.</summary>
public sealed record ContactSelection(SubscribedContactDto Contact)
{
    public bool IsSelected { get; set; }
}

/// <summary>Result returned when the teacher confirms publish (spec §8/§12).</summary>
public sealed record PublishResult(IReadOnlyList<Guid>? ContactIds);
