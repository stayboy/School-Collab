using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Api.Endpoints;
using SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Commands.UpsertGradeAssignmentPolicy;
using SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Queries.GetGradeAssignmentPolicy;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Api.Tests.Unit;

/// <summary>
/// Round A (<c>documents/solution/assignment-policy-fields.md</c> §4/§5) — the per-grade
/// assignment-policy endpoint's <b>wire binding</b>, pinned at the route with a minimal TestServer
/// harness (the <c>WardRoutesTests</c> precedent: only <c>MapGradeLevelRoutes</c> is mapped and
/// every handler is a stub, so no database or broker is reached).
///
/// <para>Three things a handler test cannot show:</para>
/// <list type="bullet">
///   <item>the PUT body binds the whole new field set — including
///   <see cref="SignatureRequirementMode"/> travelling as its <b>name</b>;</item>
///   <item>an absent/null field binds as <b>inherit</b> rather than as a default;</item>
///   <item>the pre-widening boolean body (<c>requiresSignatureDefault</c>) the shipped Admin editor
///   still sends maps as D8 specifies — <c>true → Optional</c>, <c>false → Disabled</c> — and a new
///   field, when present, wins over it (Round A compatibility; Round B deletes it).</item>
/// </list>
/// </summary>
[TestClass]
public class GradeAssignmentPolicyRoutesTests
{
    private static readonly Guid GradeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static string PolicyUrl => $"/students/grade-levels/{GradeId}/assignment-policy";

    private sealed class CapturingUpsertHandler : ICommandHandler<UpsertGradeAssignmentPolicy, GradeAssignmentPolicyDto>
    {
        public UpsertGradeAssignmentPolicy? Last { get; private set; }

        public Task<GradeAssignmentPolicyDto> HandleAsync(
            UpsertGradeAssignmentPolicy command, CancellationToken ct = default)
        {
            Last = command;
            return Task.FromResult(new GradeAssignmentPolicyDto(
                command.GradeLevelId, command.SignatureRequirement, command.RequiresApprovalBeforePublish,
                command.MaxPrimaryContacts, command.MaxCopyContacts, command.MandatoryReview,
                command.ArchiveGraceDays, DateTimeOffset.UtcNow));
        }
    }

    private sealed class StubGetHandler(GradeAssignmentPolicyDto? result)
        : IQueryHandler<GetGradeAssignmentPolicy, GradeAssignmentPolicyDto?>
    {
        public Task<GradeAssignmentPolicyDto?> HandleAsync(GetGradeAssignmentPolicy query, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    /// <summary>
    /// Upsert stub that reproduces the real handler's Q7 guard: a non-positive contact cap
    /// throws <see cref="ArgumentOutOfRangeException"/>. The guard itself is covered by
    /// <c>GradeAssignmentPolicyHandlerTests</c>; this stub exists so the route's catch arm can be
    /// pinned at the wire without a database.
    /// </summary>
    private sealed class CapGuardingUpsertHandler : ICommandHandler<UpsertGradeAssignmentPolicy, GradeAssignmentPolicyDto>
    {
        public Task<GradeAssignmentPolicyDto> HandleAsync(
            UpsertGradeAssignmentPolicy command, CancellationToken ct = default)
        {
            if (command.MaxPrimaryContacts is <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(command.MaxPrimaryContacts), command.MaxPrimaryContacts, "non-positive cap");
            if (command.MaxCopyContacts is <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(command.MaxCopyContacts), command.MaxCopyContacts, "non-positive cap");

            return Task.FromResult(new GradeAssignmentPolicyDto(
                command.GradeLevelId, command.SignatureRequirement, command.RequiresApprovalBeforePublish,
                command.MaxPrimaryContacts, command.MaxCopyContacts, command.MandatoryReview,
                command.ArchiveGraceDays, DateTimeOffset.UtcNow));
        }
    }

    private sealed class Harness
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }
        public required CapturingUpsertHandler Upsert { get; init; }
    }

    private static async Task<WebApplication> StartWithGuardingUpsertAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<ICommandHandler<UpsertGradeAssignmentPolicy, GradeAssignmentPolicyDto>>(
            new CapGuardingUpsertHandler());

        var app = builder.Build();
        app.MapGroup("/students").MapGradeLevelRoutes();
        await app.StartAsync();
        return app;
    }

    private static async Task<Harness> StartAsync(GradeAssignmentPolicyDto? getResult = null)
    {
        var upsert = new CapturingUpsertHandler();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<ICommandHandler<UpsertGradeAssignmentPolicy, GradeAssignmentPolicyDto>>(upsert);
        builder.Services.AddSingleton<IQueryHandler<GetGradeAssignmentPolicy, GradeAssignmentPolicyDto?>>(
            new StubGetHandler(getResult));

        var app = builder.Build();
        app.MapGroup("/students").MapGradeLevelRoutes();
        await app.StartAsync();

        return new Harness
        {
            App = app,
            Client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient(),
            Upsert = upsert,
        };
    }

    private static async Task<(HttpStatusCode Status, CapturingUpsertHandler Upsert)> PutAsync(string json)
    {
        var harness = await StartAsync();
        await using (harness.App)
        {
            var response = await harness.Client.PutAsync(
                PolicyUrl, new StringContent(json, Encoding.UTF8, "application/json"));
            return (response.StatusCode, harness.Upsert);
        }
    }

    [TestMethod]
    public async Task Put_WithTheWholeFieldSet_BindsEveryField()
    {
        var (status, upsert) = await PutAsync("""
            {"signatureRequirement":"Mandatory","requiresApprovalBeforePublish":true,
             "maxPrimaryContacts":2,"maxCopyContacts":4,
             "mandatoryReview":true,"archiveGraceDays":45}
            """);

        status.Should().Be(HttpStatusCode.OK);
        upsert.Last.Should().NotBeNull();
        upsert.Last!.GradeLevelId.Should().Be(GradeId, "the route id is bound to the command");
        upsert.Last.SignatureRequirement.Should().Be(SignatureRequirementMode.Mandatory,
            "the enum travels as its name, the type-level converter's wire form");
        upsert.Last.RequiresApprovalBeforePublish.Should().BeTrue();
        upsert.Last.MaxPrimaryContacts.Should().Be(2);
        upsert.Last.MaxCopyContacts.Should().Be(4);
        upsert.Last.MandatoryReview.Should().BeTrue("the PUT body binds the guardian-review field (D3, AC7)");
        upsert.Last.ArchiveGraceDays.Should().Be(45, "and the archive window (D5, AC7)");
    }

    /// <summary>D3/D5/AC7: the two new fields are optional on the override PUT — an absent field means
    /// "inherit the tenant default", never a fabricated value.</summary>
    [TestMethod]
    public async Task Put_WithoutTheNewFields_BindsInheritForThem()
    {
        var (status, upsert) = await PutAsync("""{"maxPrimaryContacts":3}""");

        status.Should().Be(HttpStatusCode.OK);
        upsert.Last!.MandatoryReview.Should().BeNull();
        upsert.Last.ArchiveGraceDays.Should().BeNull();
    }

    [TestMethod]
    public async Task Put_WithAbsentFields_BindsInheritForEveryField()
    {
        var (status, upsert) = await PutAsync("{}");

        status.Should().Be(HttpStatusCode.OK);
        upsert.Last!.SignatureRequirement.Should().BeNull("absent means inherit the tenant default");
        upsert.Last.RequiresApprovalBeforePublish.Should().BeNull();
        upsert.Last.MaxPrimaryContacts.Should().BeNull();
        upsert.Last.MaxCopyContacts.Should().BeNull();
    }

    [TestMethod]
    public async Task Put_WithTheLegacyBooleanTrue_BindsOptional()
    {
        var (status, upsert) = await PutAsync("""{"requiresSignatureDefault":true}""");

        status.Should().Be(HttpStatusCode.OK);
        upsert.Last!.SignatureRequirement.Should().Be(SignatureRequirementMode.Optional,
            "D8: true → Optional (the shipped editor's 'Require signature')");
    }

    [TestMethod]
    public async Task Put_WithTheLegacyBooleanFalse_BindsDisabled()
    {
        var (status, upsert) = await PutAsync("""{"requiresSignatureDefault":false}""");

        status.Should().Be(HttpStatusCode.OK);
        upsert.Last!.SignatureRequirement.Should().Be(SignatureRequirementMode.Disabled,
            "D8: false → Disabled (the shipped editor's 'No signature required')");
    }

    [TestMethod]
    public async Task Put_WithTheLegacyNull_BindsInherit()
    {
        var (status, upsert) = await PutAsync("""{"requiresSignatureDefault":null}""");

        status.Should().Be(HttpStatusCode.OK);
        upsert.Last!.SignatureRequirement.Should().BeNull(
            "the shipped editor's 'Inherit global' sends the legacy null, which must stay inherit");
    }

    [TestMethod]
    public async Task Put_WithBothShapes_TheNewFieldWins()
    {
        var (status, upsert) = await PutAsync(
            """{"signatureRequirement":"Disabled","requiresSignatureDefault":true}""");

        status.Should().Be(HttpStatusCode.OK);
        upsert.Last!.SignatureRequirement.Should().Be(SignatureRequirementMode.Disabled,
            "the legacy member is input-only compatibility; a supplied new field always wins");
    }

    [TestMethod]
    public async Task Put_WithoutSigRequirement_LeavesOnlyTheSignatureFieldUnset()
    {
        var (status, upsert) = await PutAsync("""{"maxPrimaryContacts":3}""");

        status.Should().Be(HttpStatusCode.OK);
        upsert.Last!.MaxPrimaryContacts.Should().Be(3);
        upsert.Last.SignatureRequirement.Should().BeNull(
            "an omitted legacy bool must not fabricate a signature value");
    }

    // ── Q7 — a non-positive contact cap is a 400 on the wire ─────

    [TestMethod]
    [DataRow("""{"maxPrimaryContacts":0}""")]
    [DataRow("""{"maxPrimaryContacts":-3}""")]
    [DataRow("""{"maxCopyContacts":0}""")]
    [DataRow("""{"maxCopyContacts":-1}""")]
    public async Task Put_WithANonPositiveCap_Returns400(string json)
    {
        var app = await StartWithGuardingUpsertAsync();

        await using (app)
        {
            var client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();
            var response = await client.PutAsync(
                PolicyUrl, new StringContent(json, Encoding.UTF8, "application/json"));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "Q7: a non-positive cap is rejected on the write path, not stored");
        }
    }

    [TestMethod]
    public async Task Put_WithPositiveCaps_Returns200()
    {
        var app = await StartWithGuardingUpsertAsync();

        await using (app)
        {
            var client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();
            var response = await client.PutAsync(PolicyUrl, new StringContent(
                """{"maxPrimaryContacts":2,"maxCopyContacts":4}""", Encoding.UTF8, "application/json"));

            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "a valid fields body still writes through");
        }
    }

    [TestMethod]
    public async Task Get_WhenTheGradeHasNoOverrideRow_Returns204()
    {
        var harness = await StartAsync(getResult: null);

        await using (harness.App)
        {
            var response = await harness.Client.GetAsync(PolicyUrl);
            response.StatusCode.Should().Be(HttpStatusCode.NoContent,
                "no row means the grade inherits — the pre-existing 204 contract is unchanged");
        }
    }

    [TestMethod]
    public async Task Get_WhenTheGradeHasAnOverride_ReturnsTheWidenedFieldSet()
    {
        var harness = await StartAsync(new GradeAssignmentPolicyDto(
            GradeId, SignatureRequirementMode.Optional, RequiresApprovalBeforePublish: true,
            MaxPrimaryContacts: 2, MaxCopyContacts: null, MandatoryReview: null, ArchiveGraceDays: 45,
            DateTimeOffset.UtcNow));

        await using (harness.App)
        {
            var response = await harness.Client.GetAsync(PolicyUrl);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var dto = JsonSerializer.Deserialize<GradeAssignmentPolicyDto>(
                await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

            dto!.SignatureRequirement.Should().Be(SignatureRequirementMode.Optional);
            dto.RequiresApprovalBeforePublish.Should().BeTrue();
            dto.MaxPrimaryContacts.Should().Be(2);
            dto.MaxCopyContacts.Should().BeNull();
            dto.RequiresSignatureDefault.Should().BeTrue(
                "the compat member is derived, never serialized (JsonIgnore)");
        }
    }
}
