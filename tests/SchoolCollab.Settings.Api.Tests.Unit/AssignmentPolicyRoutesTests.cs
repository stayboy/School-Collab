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
using SchoolCollab.Settings.Api.Endpoints;
using SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Commands.UpsertTenantAssignmentPolicy;
using SchoolCollab.Settings.Core.CQRS.AssignmentPolicies.Queries.GetTenantAssignmentPolicy;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Tests.Unit;

/// <summary>
/// Round B1 (<c>documents/rounds/round-assignment-policy-ui.md</c> Plan (f) AC7) — the
/// <c>PUT /api/settings/assignment-policy</c> route's <b>status mapping</b>: the Q7 non-positive
/// contact-cap guard throws <see cref="ArgumentOutOfRangeException"/> in the handler and the route
/// maps it to <b>400</b> (the notification-policy PUT precedent), while a valid fields body still
/// returns 200. The guard itself is covered by
/// <c>SchoolCollab.Settings.Tests.Unit.Handlers.TenantAssignmentPolicyHandlerTests</c>; this file
/// pins the wire, mirroring <c>GradeAssignmentPolicyRoutesTests</c> (minimal TestServer harness —
/// only the assignment-policy group is mapped, every handler is a stub, so no database is reached).
/// </summary>
[TestClass]
public class AssignmentPolicyRoutesTests
{
    private const string PolicyUrl = "/api/settings/assignment-policy";

    private sealed class CapturingUpsertHandler
        : ICommandHandler<UpsertTenantAssignmentPolicy, TenantAssignmentPolicyDto>
    {
        public UpsertTenantAssignmentPolicy? Last { get; private set; }

        public Task<TenantAssignmentPolicyDto> HandleAsync(
            UpsertTenantAssignmentPolicy command, CancellationToken ct = default)
        {
            Last = command;
            return Task.FromResult(new TenantAssignmentPolicyDto(
                command.SignatureRequirement, command.RequiresApprovalBeforePublish,
                command.MaxPrimaryContacts, command.MaxCopyContacts));
        }
    }

    /// <summary>
    /// Upsert stub that reproduces the real handler's Q7 guard: a non-positive contact cap throws
    /// <see cref="ArgumentOutOfRangeException"/>. Its purpose is to reach the route's catch arm.
    /// </summary>
    private sealed class CapGuardingUpsertHandler
        : ICommandHandler<UpsertTenantAssignmentPolicy, TenantAssignmentPolicyDto>
    {
        public Task<TenantAssignmentPolicyDto> HandleAsync(
            UpsertTenantAssignmentPolicy command, CancellationToken ct = default)
        {
            if (command.MaxPrimaryContacts is <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(command.MaxPrimaryContacts), command.MaxPrimaryContacts, "non-positive cap");
            if (command.MaxCopyContacts is <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(command.MaxCopyContacts), command.MaxCopyContacts, "non-positive cap");

            return Task.FromResult(new TenantAssignmentPolicyDto(
                command.SignatureRequirement, command.RequiresApprovalBeforePublish,
                command.MaxPrimaryContacts, command.MaxCopyContacts));
        }
    }

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        ICommandHandler<UpsertTenantAssignmentPolicy, TenantAssignmentPolicyDto> upsert,
        TenantAssignmentPolicyDto? getResult = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(upsert);
        builder.Services.AddSingleton<IQueryHandler<GetTenantAssignmentPolicy, TenantAssignmentPolicyDto?>>(
            new StubGetHandler(getResult));

        var app = builder.Build();
        app.MapGroup("/api/settings").MapAssignmentPolicyRoutes();
        await app.StartAsync();

        return (app, ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient());
    }

    private sealed class StubGetHandler(TenantAssignmentPolicyDto? result)
        : IQueryHandler<GetTenantAssignmentPolicy, TenantAssignmentPolicyDto?>
    {
        public Task<TenantAssignmentPolicyDto?> HandleAsync(
            GetTenantAssignmentPolicy query, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private static async Task<HttpResponseMessage> PutAsync(
        ICommandHandler<UpsertTenantAssignmentPolicy, TenantAssignmentPolicyDto> upsert, string json)
    {
        var (app, client) = await StartAsync(upsert);
        await using (app)
        {
            return await client.PutAsync(PolicyUrl, new StringContent(json, Encoding.UTF8, "application/json"));
        }
    }

    // ── Q7 — a non-positive contact cap is a 400 on the wire ─────

    [TestMethod]
    [DataRow("""{"maxPrimaryContacts":0}""")]
    [DataRow("""{"maxPrimaryContacts":-3}""")]
    [DataRow("""{"maxCopyContacts":0}""")]
    [DataRow("""{"maxCopyContacts":-1}""")]
    public async Task Put_WithANonPositiveCap_Returns400(string json)
    {
        var response = await PutAsync(new CapGuardingUpsertHandler(), json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "Q7: a non-positive cap is rejected on the write path, not stored");
    }

    // ── A valid fields body still writes through ─────

    [TestMethod]
    public async Task Put_WithAValidFieldsBody_Returns200AndBindsEveryField()
    {
        var upsert = new CapturingUpsertHandler();

        var response = await PutAsync(upsert, """
            {"signatureRequirement":"Mandatory","requiresApprovalBeforePublish":true,
             "maxPrimaryContacts":2,"maxCopyContacts":4,
             "mandatoryReview":true,"archiveGraceDays":45}
            """);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        upsert.Last.Should().NotBeNull();
        upsert.Last!.SignatureRequirement.Should().Be(SignatureRequirementMode.Mandatory,
            "the enum travels as its name, the type-level converter's wire form");
        upsert.Last.RequiresApprovalBeforePublish.Should().BeTrue();
        upsert.Last.MaxPrimaryContacts.Should().Be(2);
        upsert.Last.MaxCopyContacts.Should().Be(4);
        upsert.Last.MandatoryReview.Should().BeTrue("the PUT body binds the guardian-review field (D3, AC7)");
        upsert.Last.ArchiveGraceDays.Should().Be(45, "and the archive window (D5, AC7)");
    }

    /// <summary>D3/D5/AC7: the two new fields are optional — a body that omits them leaves them unset
    /// rather than failing to bind.</summary>
    [TestMethod]
    public async Task Put_WithoutTheNewFields_LeavesThemUnset()
    {
        var upsert = new CapturingUpsertHandler();

        var response = await PutAsync(upsert, """{"signatureRequirement":"Optional"}""");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        upsert.Last!.MandatoryReview.Should().BeNull("a body without the field binds null = unset");
        upsert.Last.ArchiveGraceDays.Should().BeNull();
    }

    [TestMethod]
    public async Task Put_WithPositiveCapsOnly_Returns200()
    {
        var upsert = new CapturingUpsertHandler();

        var response = await PutAsync(upsert, """{"maxPrimaryContacts":2,"maxCopyContacts":4}""");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        upsert.Last!.MaxPrimaryContacts.Should().Be(2);
        upsert.Last.MaxCopyContacts.Should().Be(4);
    }

    [TestMethod]
    public async Task Get_WhenTheTenantHasNoPolicyRow_Returns204()
    {
        var (app, client) = await StartAsync(new CapturingUpsertHandler(), getResult: null);

        await using (app)
        {
            var response = await client.GetAsync(PolicyUrl);
            response.StatusCode.Should().Be(HttpStatusCode.NoContent,
                "no row means unset — the caller applies the built-in defaults");
        }
    }

    [TestMethod]
    public async Task Get_WhenTheTenantHasAPolicyRow_ReturnsTheFieldSet()
    {
        var stored = new TenantAssignmentPolicyDto(
            SignatureRequirementMode.Optional, RequiresApprovalBeforePublish: true,
            MaxPrimaryContacts: 2, MaxCopyContacts: null,
            MandatoryReview: false, ArchiveGraceDays: 45);
        var (app, client) = await StartAsync(new CapturingUpsertHandler(), stored);

        await using (app)
        {
            var response = await client.GetAsync(PolicyUrl);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var dto = JsonSerializer.Deserialize<TenantAssignmentPolicyDto>(
                await response.Content.ReadAsStringAsync(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            dto!.SignatureRequirement.Should().Be(SignatureRequirementMode.Optional);
            dto.MaxPrimaryContacts.Should().Be(2);
            dto.MaxCopyContacts.Should().BeNull();
            dto.MandatoryReview.Should().BeFalse("the GET body carries the new field set too");
            dto.ArchiveGraceDays.Should().Be(45);
        }
    }
}
