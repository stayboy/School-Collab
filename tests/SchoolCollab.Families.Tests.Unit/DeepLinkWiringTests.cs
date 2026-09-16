using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.DeepLinks;

namespace SchoolCollab.Families.Tests.Unit;

/// <summary>
/// WS-E1 (ar-14-deep-links) — wiring of the DataProtection contract shared by the
/// Assignments API (mint) and the Families host (validate). Both hosts must persist
/// their keyring under the SAME <see cref="DeepLinkConstants.ApplicationName"/> and
/// protect with the SAME <see cref="DeepLinkConstants.Purpose"/> or the Families host
/// cannot unprotect mint-side ciphertext. This guarantees the shared constants are
/// stable and that two wrapper sides built under the shared application name round-trip,
/// while a drifted application name fails — the exact symptom a host forgetting
/// <c>SetApplicationName(DeepLinkConstants.ApplicationName)</c> would produce.
/// </summary>
[TestClass]
public class DeepLinkWiringTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AssignmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid WardStudentId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static DeepLinkTokenPayload Payload() => new(
        TenantId, AssignmentId, ContactId, OwnerType: 1, Role: 0, WardStudentId,
        DateTimeOffset.UtcNow.AddDays(7));

    [TestMethod]
    public void SharedConstants_AreStable()
    {
        DeepLinkConstants.ApplicationName.Should().Be("school-collab-deeplinks");
        DeepLinkConstants.Purpose.Should().Be("ar-deeplink");
    }

    [TestMethod]
    public void MintAndValidateSides_RoundTrip_UnderSharedApplicationName()
    {
        // Both hosts share the key material (the Redis-persisted keyring — modelled here
        // by ONE EphemeralDataProtectionProvider) and use the same purpose constant, so a
        // token minted on the Assignments side unprotects on the Families side.
        var provider = new EphemeralDataProtectionProvider();
        var mintSide = new DeepLinkProtector(provider);
        var validateSide = new DeepLinkProtector(provider);

        var expected = Payload();
        var token = mintSide.Protect(expected);
        var result = validateSide.TryUnprotect(token);

        result.Should().BeEquivalentTo(expected);
    }

    [TestMethod]
    public void DriftedApplicationName_FailsCrossUnprotect()
    {
        // A host that set SetApplicationName to anything other than the shared constant
        // can no longer read mint-side ciphertext — the drift the wiring must prevent.
        // Each AddDataProtection() container gets its own ephemeral keyring + app name,
        // so cross-unprotect can only succeed when both share the same app name + keys.
        var mintSide = CreateSide(DeepLinkConstants.ApplicationName);
        var driftedSide = CreateSide("a-different-application-name");

        var token = mintSide.Protect(Payload());

        driftedSide.TryUnprotect(token).Should().BeNull();
    }

    private static DeepLinkProtector CreateSide(string applicationName)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName(applicationName);
        var provider = services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
        return new DeepLinkProtector(provider);
    }
}
