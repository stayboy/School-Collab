using System.Text.Json;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// Claim-set shape coverage (plan step 6): claim names EXACTLY as the realm's protocol mappers
/// emit them (school-collab-realm.json), multivalued roles, required tenant/teacher binding.
/// Fixture-driven — this file deliberately does NOT claim live parity with a running Keycloak:
/// decoding and validating a real token is a runtime/cold-start concern, not a shape test
/// (the plan's honesty note for pass 3b).
/// </summary>
[TestClass]
public class ClaimSetFactoryTests
{
    // The fixture mirrors what the realm's attribute + `User Realm Role` mappers emit as claims
    // in a token payload (school-collab-realm.json): tenant_id / tenant_name / tenant_type /
    // teacher_id / roles, with roles multivalued.
    private const string DevSchoolTokenPayload = """
        {
          "tenant_id": "00000000-0000-0000-0000-000000000002",
          "tenant_name": "Dev School",
          "tenant_type": "School",
          "teacher_id": "00000000-0000-0000-0000-000000000003",
          "roles": ["user-admin"]
        }
        """;

    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement;

    [TestMethod]
    public void Build_ReadsClaimNamesExactlyAsTheRealmMappersEmitThem()
    {
        var claims = new ClaimSetFactory().Build(Payload(DevSchoolTokenPayload));

        claims.TenantId.Should().Be("00000000-0000-0000-0000-000000000002",
            "the claim name must match the realm mapper contract (tenant-id → tenant_id).");
        claims.TenantName.Should().Be("Dev School");
        claims.TenantType.Should().Be("School");
        claims.TeacherId.Should().Be("00000000-0000-0000-0000-000000000003");
    }

    [TestMethod]
    public void Build_ReadsRolesAsAMultivaluedClaim()
    {
        var claims = new ClaimSetFactory().Build(Payload("""
            { "tenant_id": "t", "tenant_name": "n", "tenant_type": "School", "teacher_id": "x", "roles": ["user-admin", "platform-admin"] }
            """));

        claims.Roles.Should().Equal(new[] { "user-admin", "platform-admin" },
            "the realm's User Realm Role mapper emits `roles` as a multivalued claim; order is preserved.");
    }

    [TestMethod]
    public void Build_WithNoRolesClaim_ProducesNoRoles()
    {
        var claims = new ClaimSetFactory().Build(Payload("""
            { "tenant_id": "t", "tenant_name": "n", "tenant_type": "School", "teacher_id": "x" }
            """));

        claims.Roles.Should().BeEmpty(
            "an absent `roles` claim must not throw — absence can only mean no realm roles.");
    }

    [TestMethod]
    public void Build_AcceptsPlainStringValues_AsTheNonMultivaluedMappersEmit()
    {
        // The realm's attribute mappers are NOT multivalued, so a token carries a plain JSON string.
        var claims = new ClaimSetFactory().Build(Payload("""
            { "tenant_id": "t1", "tenant_name": "One", "tenant_type": "Organization", "teacher_id": "te", "roles": [] }
            """));

        claims.TenantId.Should().Be("t1");
        claims.TenantName.Should().Be("One");
        claims.TenantType.Should().Be("Organization");
        claims.TeacherId.Should().Be("te");
    }

    [TestMethod]
    public void Build_MissingRequiredClaim_FailsFastNamingTheClaim()
    {
        var factory = new ClaimSetFactory();

        var act = () => factory.Build(Payload("""
            { "tenant_name": "n", "tenant_type": "School", "teacher_id": "x", "roles": [] }
            """));

        act.Should().Throw<System.IO.InvalidDataException>().WithMessage("*'tenant_id'*",
            "a session without tenant binding cannot be served — the failure must name the missing claim.");
    }

    [TestMethod]
    public void Build_NonObjectPayload_FailsFast()
    {
        var factory = new ClaimSetFactory();

        var act = () => factory.Build(Payload("\"just-a-string\""));

        act.Should().Throw<System.IO.InvalidDataException>();
    }
}
