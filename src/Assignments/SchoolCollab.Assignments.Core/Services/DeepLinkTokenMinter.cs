using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.DeepLinks;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Default <see cref="IDeepLinkTokenMinter"/>: builds the contact-scoped
/// <see cref="DeepLinkTokenPayload"/> (carrying the recipient's tenant, assignment,
/// contact, owner-type/role ints, and optional ward) and protects it via the shared
/// <see cref="DeepLinkProtector"/>. Expiry = mint time + policy <c>LinkValidityDays</c>
/// (falling back to <see cref="DeepLinkConstants.DefaultValidityDays"/>).
/// </summary>
public sealed class DeepLinkTokenMinter(DeepLinkProtector protector) : IDeepLinkTokenMinter
{
    /// <inheritdoc />
    public DeepLinkMintedToken Mint(AssignmentRecipient recipient, int? linkValidityDays, DateTimeOffset now)
    {
        var days = linkValidityDays ?? DeepLinkConstants.DefaultValidityDays;
        var expiresAt = now.AddDays(days);
        var payload = new DeepLinkTokenPayload(
            TenantId: recipient.TenantId,
            AssignmentId: recipient.AssignmentId,
            ContactId: recipient.ContactId,
            OwnerType: (int)recipient.OwnerType,
            Role: (int?)recipient.Role,
            WardStudentId: recipient.WardStudentId,
            ExpiresAt: expiresAt);
        return new DeepLinkMintedToken(protector.Protect(payload), expiresAt);
    }
}
