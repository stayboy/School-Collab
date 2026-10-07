using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Optional per-grade <b>override</b> of the assignment policy (WS-C1 / spec §7 Q1;
/// <c>documents/solution/assignment-policy-fields.md</c> §4). At most one row per
/// (tenant, grade). <b>A null field means "inherit the tenant default"</b>; a non-null field
/// overrides it. The <see cref="SchoolCollab.Core.AssignmentPolicies.EffectiveAssignmentPolicyResolver"/>
/// merges this with the tenant-global default and reports per-field source flags.
///
/// <para>The field set mirrors <see cref="SchoolCollab.Core.AssignmentPolicies.AssignmentPolicyFields"/>
/// as real columns (no JSON/owned type), exactly like <see cref="GradeNotificationPolicy"/>.</para>
/// </summary>
public sealed class GradeAssignmentPolicy : BaseTenantEntityWithAudit, IHasRowVersion
{
    private GradeAssignmentPolicy() { }

    public Guid GradeLevelId { get; private set; }

    /// <summary>
    /// Grade-level override of the signature requirement
    /// (<see cref="SignatureRequirementMode.Disabled"/> = never required,
    /// <see cref="SignatureRequirementMode.Optional"/> = pre-filled but changeable,
    /// <see cref="SignatureRequirementMode.Mandatory"/> = locked on).
    /// Null = inherit the tenant default.
    /// </summary>
    public SignatureRequirementMode? SignatureRequirement { get; private set; }

    /// <summary>
    /// Grade-level override of the publish approval requirement (D3). Null = inherit the tenant
    /// default.
    /// </summary>
    public bool? RequiresApprovalBeforePublish { get; private set; }

    /// <summary>Cap on Primary-guardian contacts per sendout. Null = inherit (uncapped).</summary>
    public int? MaxPrimaryContacts { get; private set; }

    /// <summary>Cap on other-guardian contacts per sendout. Null = inherit (uncapped).</summary>
    public int? MaxCopyContacts { get; private set; }

    /// <summary>
    /// Grade-level override of the guardian-review requirement. Null = inherit the tenant default
    /// (an unset policy leaves the choice to the author). A set
    /// <see cref="SignatureRequirement"/> also implies review (D4).
    /// </summary>
    public bool? MandatoryReview { get; private set; }

    /// <summary>
    /// Grade-level override of the archive grace window in days. Null = inherit the tenant default
    /// (unset ⇒ the built-in 30-day retention floor applies at the write seam).
    /// </summary>
    public int? ArchiveGraceDays { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// Creates the override row for a grade. A null field stores "inherit the tenant default".
    /// </summary>
    public static GradeAssignmentPolicy Create(
        Guid tenantId,
        Guid gradeLevelId,
        SignatureRequirementMode? signatureRequirement = null,
        bool? requiresApprovalBeforePublish = null,
        int? maxPrimaryContacts = null,
        int? maxCopyContacts = null,
        bool? mandatoryReview = null,
        int? archiveGraceDays = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new GradeAssignmentPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GradeLevelId = gradeLevelId,
            SignatureRequirement = signatureRequirement,
            RequiresApprovalBeforePublish = requiresApprovalBeforePublish,
            MaxPrimaryContacts = maxPrimaryContacts,
            MaxCopyContacts = maxCopyContacts,
            MandatoryReview = mandatoryReview,
            ArchiveGraceDays = archiveGraceDays,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Replaces the override. A null field restores "inherit the tenant default". Stamps
    /// <see cref="UpdatedAt"/>.
    /// </summary>
    public void SetOverride(
        SignatureRequirementMode? signatureRequirement,
        bool? requiresApprovalBeforePublish,
        int? maxPrimaryContacts,
        int? maxCopyContacts,
        bool? mandatoryReview,
        int? archiveGraceDays)
    {
        SignatureRequirement = signatureRequirement;
        RequiresApprovalBeforePublish = requiresApprovalBeforePublish;
        MaxPrimaryContacts = maxPrimaryContacts;
        MaxCopyContacts = maxCopyContacts;
        MandatoryReview = mandatoryReview;
        ArchiveGraceDays = archiveGraceDays;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
