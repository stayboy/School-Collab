namespace SchoolCollab.Core.AssignmentPolicies;

/// <summary>
/// Raw, configurable assignment-policy fields shared across bounded contexts
/// (<c>documents/solution/assignment-policy-fields.md</c> §4). Used as the input shape for
/// effective-policy resolution: a tenant-global default and an optional per-grade override
/// are both expressed as <see cref="AssignmentPolicyFields"/>. Mirrors
/// <see cref="Notifications.NotificationPolicyFields"/> — the notification-policy
/// consolidation pattern this feature follows.
///
/// <para><b>Null semantics differ by role.</b> As a <b>grade override</b>, a null field means
/// "inherit the tenant default"; as the <b>tenant default</b>, a null field means "no tenant
/// value set, built-in default applies at publish time".</para>
/// </summary>
public sealed record AssignmentPolicyFields
{
    /// <summary>Whether a guardian signature is required, optional, or disabled.</summary>
    public SignatureRequirementMode? SignatureRequirement { get; init; }

    /// <summary>Whether publish is refused until the assignment is approved (D3).</summary>
    public bool? RequiresApprovalBeforePublish { get; init; }

    /// <summary>Cap on Primary-guardian contacts included in a sendout (D4; Round B enforces).</summary>
    public int? MaxPrimaryContacts { get; init; }

    /// <summary>Cap on other-guardian contacts included in a sendout (D4; Round B enforces).</summary>
    public int? MaxCopyContacts { get; init; }

    /// <summary>A policy with every field null ("nothing configured").</summary>
    public static AssignmentPolicyFields Empty { get; } = new();
}
