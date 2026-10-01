using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Per-tenant <b>global default</b> assignment policy (assignment-request-implementation-details.md
/// §2 WS-C — the C1 prerequisite bullet; <c>documents/solution/assignment-policy-fields.md</c>).
/// One row per tenant. A per-grade <see cref="GradeAssignmentPolicy"/> (Students context) may
/// override individual fields; any field left null here is simply unset (the system applies the
/// built-in defaults at resolution time — see
/// <see cref="SchoolCollab.Core.AssignmentPolicies.EffectiveAssignmentPolicyResolver"/>).
///
/// <para>The field set mirrors <see cref="SchoolCollab.Core.AssignmentPolicies.AssignmentPolicyFields"/>
/// as real columns (no JSON/owned type), exactly like <see cref="TenantNotificationPolicy"/>.</para>
/// </summary>
public sealed class TenantAssignmentPolicy : BaseTenantEntityWithAudit, IHasRowVersion
{
    private TenantAssignmentPolicy() { }

    /// <summary>
    /// Whether a guardian signature is required by default for this tenant
    /// (<see cref="SignatureRequirementMode.Disabled"/> = never required,
    /// <see cref="SignatureRequirementMode.Optional"/> = pre-filled but changeable,
    /// <see cref="SignatureRequirementMode.Mandatory"/> = locked on). Null = unset.
    /// </summary>
    public SignatureRequirementMode? SignatureRequirement { get; private set; }

    /// <summary>
    /// Whether publishing an assignment for this tenant requires an explicit approval first.
    /// Null = unset; effective-true is OR'd with <c>FEATURE:RequireAssignmentApproval</c> for
    /// one release (D3).
    /// </summary>
    public bool? RequiresApprovalBeforePublish { get; private set; }

    /// <summary>Cap on Primary-guardian contacts per sendout. Null = unset (uncapped).</summary>
    public int? MaxPrimaryContacts { get; private set; }

    /// <summary>Cap on other-guardian contacts per sendout. Null = unset (uncapped).</summary>
    public int? MaxCopyContacts { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// Creates the single policy row for <paramref name="tenantId"/>. Callers typically use
    /// <see cref="SetPolicy"/> on an existing row instead; this factory is for the initial insert.
    /// </summary>
    public static TenantAssignmentPolicy Create(
        Guid tenantId,
        SignatureRequirementMode? signatureRequirement = null,
        bool? requiresApprovalBeforePublish = null,
        int? maxPrimaryContacts = null,
        int? maxCopyContacts = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new TenantAssignmentPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SignatureRequirement = signatureRequirement,
            RequiresApprovalBeforePublish = requiresApprovalBeforePublish,
            MaxPrimaryContacts = maxPrimaryContacts,
            MaxCopyContacts = maxCopyContacts,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Replaces the whole tenant default (a null field is stored as "unset"). Stamps
    /// <see cref="UpdatedAt"/>.
    /// </summary>
    public void SetPolicy(
        SignatureRequirementMode? signatureRequirement,
        bool? requiresApprovalBeforePublish,
        int? maxPrimaryContacts,
        int? maxCopyContacts)
    {
        SignatureRequirement = signatureRequirement;
        RequiresApprovalBeforePublish = requiresApprovalBeforePublish;
        MaxPrimaryContacts = maxPrimaryContacts;
        MaxCopyContacts = maxCopyContacts;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
