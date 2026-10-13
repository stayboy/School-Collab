using System.Text;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile.Tests.Unit;

/// <summary>
/// Covers <see cref="JwtPayloadReader"/> — the device-side claim read that backs the client-side
/// auth state. Fixture tokens are built here rather than fetched: no live Keycloak participates.
/// </summary>
[TestClass]
public class JwtPayloadReaderTests
{
    [TestMethod]
    public void ReadClaimValues_DecodesTheRawRealmClaimSpellings()
    {
        var token = Jwt("""
            {
              "tenant_id": "00000000-0000-0000-0000-000000000002",
              "tenant_name": "Dev School",
              "teacher_id": "00000000-0000-0000-0000-000000000003",
              "sub": "8f14e45f"
            }
            """);

        var claims = JwtPayloadReader.ReadClaimValues(token);

        claims["tenant_id"].Should().Equal("00000000-0000-0000-0000-000000000002");
        claims["tenant_name"].Should().Equal("Dev School");
        claims["teacher_id"].Should().Equal("00000000-0000-0000-0000-000000000003");
        claims["sub"].Should().Equal("8f14e45f");
    }

    [TestMethod]
    public void ReadClaimValues_ReadsAMultivaluedClaimAsOneValuePerArrayElement()
    {
        var token = Jwt("""{ "roles": ["teacher", "staff"] }""");

        var claims = JwtPayloadReader.ReadClaimValues(token);

        // The realm's roles mapper is multivalued; the gates and the client-side provider flatten the
        // array into one claim per role.
        claims["roles"].Should().Equal("teacher", "staff");
    }

    [TestMethod]
    public void ReadClaimValues_LeavesNonStringScalarsAsTheirJsonText()
    {
        var token = Jwt("""{ "exp": 1760000000, "email_verified": true }""");

        var claims = JwtPayloadReader.ReadClaimValues(token);

        claims["exp"].Should().Equal("1760000000");
        claims["email_verified"].Should().Equal("true");
    }

    [TestMethod]
    public void ReadClaimValues_IgnoresObjectValuedProperties()
    {
        var token = Jwt("""{ "realm_access": { "roles": ["teacher"] }, "sub": "abc" }""");

        var claims = JwtPayloadReader.ReadClaimValues(token);

        claims.Should().NotContainKey("realm_access");
        claims["sub"].Should().Equal("abc");
    }

    [TestMethod]
    public void ReadClaimValues_DoesNotVerifyTheSignature_ByDesign()
    {
        // The signature segment is deliberately garbage. Verification is the API's job
        // (AddJwtBearer) and the claims read here only drive UI visibility, so the reader must NOT
        // grow signature logic: a forged token buys nothing and would still be refused by every
        // endpoint. If this test ever fails because the reader started verifying, that is a
        // deliberate design change, not a bug fix.
        var token = Jwt("""{ "tenant_id": "00000000-0000-0000-0000-000000000002" }""", signature: "%%not-a-signature%%");

        var claims = JwtPayloadReader.ReadClaimValues(token);

        claims["tenant_id"].Should().Equal("00000000-0000-0000-0000-000000000002");
    }

    [TestMethod]
    public void ReadClaimValues_AcceptsUnpaddedBase64UrlSegments()
    {
        // A real Keycloak token's payload length is not a multiple of 3, so its base64url segment
        // carries no '=' padding — the decoder has to restore it.
        var token = Jwt("""{ "tenant_name": "Dev School" }""");

        token.Split('.')[1].Should().NotContain("=");
        JwtPayloadReader.ReadClaimValues(token)["tenant_name"].Should().Equal("Dev School");
    }

    [TestMethod]
    public void ReadClaimValues_RejectsATokenThatIsNotThreeSegments()
    {
        Action read = () => JwtPayloadReader.ReadClaimValues("only.two");

        read.Should().Throw<FormatException>();
    }

    [TestMethod]
    public void ReadClaimValues_RejectsAPayloadSegmentThatIsNotBase64Url()
    {
        Action read = () => JwtPayloadReader.ReadClaimValues("header.!!!not-base64!!!.signature");

        read.Should().Throw<FormatException>();
    }

    [TestMethod]
    public void ReadClaimValues_RejectsAPayloadThatIsNotAJsonObject()
    {
        var token = $"header.{ToBase64Url("\"just-a-string\"")}.signature";

        Action read = () => JwtPayloadReader.ReadClaimValues(token);

        read.Should().Throw<FormatException>();
    }

    [TestMethod]
    public void ReadClaimValues_RejectsAnEmptyToken()
    {
        Action read = () => JwtPayloadReader.ReadClaimValues("   ");

        read.Should().Throw<ArgumentException>();
    }

    /// <summary>Builds a three-segment JWT from a payload JSON fragment. The signature segment is
    /// never read by <see cref="JwtPayloadReader"/> (see the no-verification test).</summary>
    private static string Jwt(string payloadJson, string signature = "signature-not-verified") =>
        $"{ToBase64Url("{\"alg\":\"RS256\",\"typ\":\"JWT\"}")}.{ToBase64Url(payloadJson)}.{ToBase64Url(signature)}";

    private static string ToBase64Url(string value) => Convert
        .ToBase64String(Encoding.UTF8.GetBytes(value))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
