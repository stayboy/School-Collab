using FluentAssertions;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

[TestClass]
public class SignatureEventTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000821");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-000000000822");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-000000000823");
    private static readonly Guid GuardianId = Guid.Parse("00000000-0000-0000-0000-000000000824");

    private static SignatureEvent CreateTypedEvent(string? typedSignature)
    {
        return SignatureEvent.Create(
            TenantId, AssignmentId, StudentId, GuardianId,
            SignatureType.Typed, typedSignature,
            "192.168.1.10", "Mozilla/5.0 (Test)", "By signing I consent.");
    }

    // ── Factory shape ─────────────────────────────────────────────────────

    [TestMethod]
    public void Create_StoresImmutableAuditFields()
    {
        var signatureEvent = SignatureEvent.Create(
            TenantId, AssignmentId, StudentId, GuardianId,
            SignatureType.Click, null,
            "192.168.1.10", "Mozilla/5.0 (Test)", "By signing I consent.");

        signatureEvent.AssignmentId.Should().Be(AssignmentId);
        signatureEvent.StudentId.Should().Be(StudentId);
        signatureEvent.SignerGuardianId.Should().Be(GuardianId);
        signatureEvent.SignatureType.Should().Be(SignatureType.Click);
        signatureEvent.IpAddress.Should().Be("192.168.1.10");
        signatureEvent.UserAgent.Should().Be("Mozilla/5.0 (Test)");
        signatureEvent.ConsentTextShown.Should().Be("By signing I consent.");
        signatureEvent.SignedAt.Should().Be(signatureEvent.CreatedAt);
        signatureEvent.CertificateStoragePath.Should().BeNull();
    }

    [TestMethod]
    public void Create_TypedSignature_IsTrimmed()
    {
        var signatureEvent = CreateTypedEvent("  Jane   Doe  ");

        signatureEvent.TypedSignature.Should().Be("Jane   Doe");
    }

    [TestMethod]
    public void Create_ClickSignature_StoresNullTypedSignature()
    {
        var signatureEvent = SignatureEvent.Create(
            TenantId, AssignmentId, StudentId, GuardianId,
            SignatureType.Click, "Ignored typed text",
            "192.168.1.10", "Mozilla/5.0 (Test)", "By signing I consent.");

        signatureEvent.TypedSignature.Should().BeNull();
    }

    // ── Validation ────────────────────────────────────────────────────────

    [TestMethod]
    public void Create_TypedWithoutName_Throws()
    {
        var act = () => CreateTypedEvent(null);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("typedSignature");
    }

    [TestMethod]
    public void Create_EmptyIpAddress_Throws()
    {
        var act = () => SignatureEvent.Create(
            TenantId, AssignmentId, StudentId, GuardianId,
            SignatureType.Click, null,
            "  ", "Mozilla/5.0 (Test)", "Consent.");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("ipAddress");
    }

    [TestMethod]
    public void Create_EmptyUserAgent_Throws()
    {
        var act = () => SignatureEvent.Create(
            TenantId, AssignmentId, StudentId, GuardianId,
            SignatureType.Click, null,
            "192.168.1.10", " ", "Consent.");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("userAgent");
    }

    [TestMethod]
    public void Create_EmptyConsentText_Throws()
    {
        var act = () => SignatureEvent.Create(
            TenantId, AssignmentId, StudentId, GuardianId,
            SignatureType.Click, null,
            "192.168.1.10", "Mozilla/5.0 (Test)", " ");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("consentTextShown");
    }

    [TestMethod]
    public void Create_EmptyGuardian_Throws()
    {
        var act = () => SignatureEvent.Create(
            TenantId, AssignmentId, StudentId, Guid.Empty,
            SignatureType.Click, null,
            "192.168.1.10", "Mozilla/5.0 (Test)", "Consent.");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("signerGuardianId");
    }
}