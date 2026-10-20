using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>Form state for the assignment publish dialog (spec §12, §3.7, §3.8).</summary>
/// <param name="Contacts">The resolved subscribed contacts the author may limit the sendout to.</param>
/// <param name="AudienceSummary">The derived audience line shown at the top of the dialog.</param>
/// <param name="MandatoryReview">Whether the effective policy requires guardian review.</param>
/// <param name="Options">The <c>GET /assignments/{id}/publish-options</c> read — the medium's blocked
/// channels and each stamped slot's ordered candidates. <c>null</c> when that read failed (fail-open):
/// the dialog then stamps nothing and publishes per policy.</param>
public sealed record PublishFormModel(
    IReadOnlyList<SubscribedContactDto> Contacts,
    string AudienceSummary,
    bool MandatoryReview,
    PublishOptionsDto? Options = null)
{
    public bool LimitSelection { get; set; }

    /// <summary>Mutable per-contact selection (FluentCheckbox binds to IsSelected).</summary>
    public List<ContactSelection> Selections { get; set; } = new();

    /// <summary>The per-sendout medium (spec §3.8 / Q19). Default: apply the policy.</summary>
    public PublishChannel Channel { get; set; } = PublishChannel.PerPolicy;

    /// <summary>The confirmed Email template, pre-selected in <c>OnModelInitialized</c> from the
    /// assignment's stored stamp (falling back to the first applicable candidate). <c>null</c> = the
    /// built-in default renders — nothing is stamped.</summary>
    public Guid? EmailTemplateId { get; set; }

    /// <summary>The confirmed SMS template (and WhatsApp's, which shares the SMS body).</summary>
    public Guid? SmsTemplateId { get; set; }
}

/// <summary>One subscribed contact plus its checkbox state.</summary>
public sealed record ContactSelection(SubscribedContactDto Contact)
{
    public bool IsSelected { get; set; }
}

/// <summary>Result returned when the teacher confirms publish (spec §8/§12, §3.7/§3.8).
/// The template ids carry only picks that appeared in the server's own candidate list, so a stale
/// stamp can never reach the request.</summary>
public sealed record PublishResult(
    IReadOnlyList<Guid>? ContactIds,
    PublishChannel Channels = PublishChannel.PerPolicy,
    Guid? PublishEmailTemplateId = null,
    Guid? PublishSmsTemplateId = null);
