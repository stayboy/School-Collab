using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Api.Endpoints;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.CreateTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.UpdateTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTemplateAuditEntries;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplates;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Tests.Unit;

/// <summary>
/// Pins the notification-template wire surface (assignment-notification-templates spec §8:
/// "TemplateAuditEntry written on create/update/delete with before/after — Settings endpoint
/// tests"). Mirrors the <see cref="AssignmentPolicyRoutesTests"/> harness: only the template
/// route group is mapped and every handler is a stub, so no database is reached.
/// <para>
/// What this file covers that the handler tests cannot: <b>enum route binding</b>
/// (<c>{kind}</c>/<c>{channel}</c> travel as names — a wrong or missing parse is a silent
/// 404/400 at runtime with every unit test still green), the by-id writer shape (round
/// notification-templates-b2c: <c>POST</c>, <c>PUT /{id}</c>, <c>DELETE /{id}</c>), the 204-vs-200
/// get-one contract, the 409 conflict arm, and the required-reason 400 on the audited tombstone.
/// </para>
/// </summary>
[TestClass]
public class NotificationTemplateRoutesTests
{
    private const string BaseUrl = "/api/settings/notification-templates";

    private static readonly Guid TemplateId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static TenantNotificationTemplateDto SampleDto(
        NotificationTemplateKind kind = NotificationTemplateKind.Publish,
        NotificationChannel channel = NotificationChannel.Email) =>
        new(TemplateId, kind, channel, "Subject for {{Title}}", "Body for {{Title}}",
            [new TemplateConstraintDto(Guid.NewGuid(), TemplateConstraintDimension.GradeLevel, "Grade 3")],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1));

    // ── Stubs ──────────────────────────────────────────────────────────────

    private sealed class GetAllHandler(IReadOnlyList<TenantNotificationTemplateDto> rows)
        : IQueryHandler<GetTenantNotificationTemplates, IReadOnlyList<TenantNotificationTemplateDto>>
    {
        public Task<IReadOnlyList<TenantNotificationTemplateDto>> HandleAsync(
            GetTenantNotificationTemplates query, CancellationToken ct = default) =>
            Task.FromResult(rows);
    }

    private sealed class GetOneHandler(TenantNotificationTemplateDto? result)
        : IQueryHandler<GetTenantNotificationTemplate, TenantNotificationTemplateDto?>
    {
        public GetTenantNotificationTemplate? Last { get; private set; }

        public Task<TenantNotificationTemplateDto?> HandleAsync(
            GetTenantNotificationTemplate query, CancellationToken ct = default)
        {
            Last = query;
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingCreateHandler : ICommandHandler<CreateTenantNotificationTemplate, TenantNotificationTemplateDto>
    {
        public CreateTenantNotificationTemplate? Last { get; private set; }

        public Task<TenantNotificationTemplateDto> HandleAsync(
            CreateTenantNotificationTemplate command, CancellationToken ct = default)
        {
            Last = command;
            return Task.FromResult(SampleDto(command.Request.Kind, command.Request.Channel));
        }
    }

    /// <summary>Reproduces the domain's WhatsApp guard so the route's 400 arm is reached.</summary>
    private sealed class ChannelGuardingCreateHandler
        : ICommandHandler<CreateTenantNotificationTemplate, TenantNotificationTemplateDto>
    {
        public Task<TenantNotificationTemplateDto> HandleAsync(
            CreateTenantNotificationTemplate command, CancellationToken ct = default)
        {
            if (command.Request.Channel == NotificationChannel.WhatsApp)
                throw new ArgumentOutOfRangeException(nameof(command), "WhatsApp is not a template channel");

            return Task.FromResult(SampleDto(command.Request.Kind, command.Request.Channel));
        }
    }

    /// <summary>Reproduces the tenant-default invariant so the route's 409 arm is reached.</summary>
    private sealed class ConflictingCreateHandler
        : ICommandHandler<CreateTenantNotificationTemplate, TenantNotificationTemplateDto>
    {
        public Task<TenantNotificationTemplateDto> HandleAsync(
            CreateTenantNotificationTemplate command, CancellationToken ct = default) =>
            throw new TenantNotificationTemplateConflictException("A default Publish/Email template already exists.");
    }

    private sealed class CapturingUpdateHandler : ICommandHandler<UpdateTenantNotificationTemplate, TenantNotificationTemplateDto>
    {
        public UpdateTenantNotificationTemplate? Last { get; private set; }

        public Task<TenantNotificationTemplateDto> HandleAsync(
            UpdateTenantNotificationTemplate command, CancellationToken ct = default)
        {
            Last = command;
            return Task.FromResult(SampleDto());
        }
    }

    private sealed class MissingUpdateHandler : ICommandHandler<UpdateTenantNotificationTemplate, TenantNotificationTemplateDto>
    {
        public Task<TenantNotificationTemplateDto> HandleAsync(
            UpdateTenantNotificationTemplate command, CancellationToken ct = default) =>
            throw new TenantNotificationTemplateNotFoundException(command.TemplateId);
    }

    private sealed class CapturingDeleteHandler : ICommandHandler<DeleteTenantNotificationTemplate>
    {
        public DeleteTenantNotificationTemplate? Last { get; private set; }

        public Task HandleAsync(DeleteTenantNotificationTemplate command, CancellationToken ct = default)
        {
            Last = command;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuditHandler(IReadOnlyList<TemplateAuditEntryDto> rows)
        : IQueryHandler<GetTemplateAuditEntries, IReadOnlyList<TemplateAuditEntryDto>>
    {
        public Task<IReadOnlyList<TemplateAuditEntryDto>> HandleAsync(
            GetTemplateAuditEntries query, CancellationToken ct = default) =>
            Task.FromResult(rows);
    }

    // ── Harness ────────────────────────────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        IReadOnlyList<TenantNotificationTemplateDto>? all = null,
        TenantNotificationTemplateDto? single = null,
        ICommandHandler<CreateTenantNotificationTemplate, TenantNotificationTemplateDto>? create = null,
        ICommandHandler<UpdateTenantNotificationTemplate, TenantNotificationTemplateDto>? update = null,
        ICommandHandler<DeleteTenantNotificationTemplate>? delete = null,
        IReadOnlyList<TemplateAuditEntryDto>? audit = null,
        GetOneHandler? getOneSpy = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton<IQueryHandler<GetTenantNotificationTemplates, IReadOnlyList<TenantNotificationTemplateDto>>>(
            new GetAllHandler(all ?? []));
        builder.Services.AddSingleton<IQueryHandler<GetTenantNotificationTemplate, TenantNotificationTemplateDto?>>(
            getOneSpy ?? new GetOneHandler(single));
        builder.Services.AddSingleton(create ?? new CapturingCreateHandler());
        builder.Services.AddSingleton(update ?? new CapturingUpdateHandler());
        builder.Services.AddSingleton(delete ?? new CapturingDeleteHandler());
        builder.Services.AddSingleton<IQueryHandler<GetTemplateAuditEntries, IReadOnlyList<TemplateAuditEntryDto>>>(
            new StubAuditHandler(audit ?? []));

        var app = builder.Build();
        app.MapGroup("/api/settings").MapNotificationTemplateRoutes();
        await app.StartAsync();

        return (app, ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient());
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    // ── Get ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetAll_Returns200AndTheRows()
    {
        var (app, client) = await StartAsync(all: [SampleDto()]);
        await using (app)
        {
            var response = await client.GetAsync(BaseUrl);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Body for {{Title}}");
        }
    }

    [TestMethod]
    public async Task GetOne_Returns204WhenNoDefaultRowIsConfigured()
    {
        // Spec §5: "204 = unset (built-in default applies)"; round b2c narrows this read to the
        // tenant DEFAULT row, so constrained-only slots also answer 204.
        var (app, client) = await StartAsync(single: null);
        await using (app)
        {
            var response = await client.GetAsync($"{BaseUrl}/Publish/Email");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
    }

    [TestMethod]
    public async Task GetOne_BindsKindAndChannelFromTheRouteAsNames()
    {
        var spy = new GetOneHandler(SampleDto(NotificationTemplateKind.Overdue, NotificationChannel.SMS));
        var (app, client) = await StartAsync(getOneSpy: spy);
        await using (app)
        {
            var response = await client.GetAsync($"{BaseUrl}/Overdue/SMS");

            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "the enum route values travel as NAMES; a failed parse would 404/400 here while "
                + "every handler unit test stayed green");
            spy.Last.Should().NotBeNull();
            spy.Last!.Kind.Should().Be(NotificationTemplateKind.Overdue);
            spy.Last.Channel.Should().Be(NotificationChannel.SMS);
        }
    }

    // ── Post (create) ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task Post_BindsTheBodyAndPassesTheReason()
    {
        var create = new CapturingCreateHandler();
        var (app, client) = await StartAsync(create: create);
        await using (app)
        {
            // The Settings host registers no JsonStringEnumConverter, so a client sends the enum's
            // numeric value in a BODY (route values still bind by name, as the tests above pin):
            // kind 1 = Reminder, channel 0 = Email.
            var response = await client.PostAsync(BaseUrl, Json("""
                {"kind":1,"channel":0,"subject":"Reminder: {{Title}}","body":"Due {{DueDate}}","constraints":[],"reason":"tweak"}
                """));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            create.Last.Should().NotBeNull();
            create.Last!.Request.Kind.Should().Be(NotificationTemplateKind.Reminder);
            create.Last.Request.Channel.Should().Be(NotificationChannel.Email);
            create.Last.Request.Body.Should().Be("Due {{DueDate}}");
            create.Last.Reason.Should().Be("tweak");
        }
    }

    [TestMethod]
    public async Task Post_Returns400WhenTheHandlerRejectsTheChannel()
    {
        // The domain rejects WhatsApp (spec §2 routes WhatsApp at the SMS body); the
        // route catches ArgumentException — of which ArgumentOutOfRangeException is one.
        var (app, client) = await StartAsync(create: new ChannelGuardingCreateHandler());
        await using (app)
        {
            // channel 2 = WhatsApp (the only value the domain rejects).
            var response = await client.PostAsync(BaseUrl, Json("""
                {"kind":0,"channel":2,"subject":null,"body":"Publish: {{Title}}","constraints":[]}
                """));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("WhatsApp",
                "the 400 must come from the domain guard, not from body binding");
        }
    }

    [TestMethod]
    public async Task Post_Returns409WhenASecondDefaultRowWouldExist()
    {
        // Round b2c WS2: an empty constraint set IS the tenant default, so a second one conflicts.
        var (app, client) = await StartAsync(create: new ConflictingCreateHandler());
        await using (app)
        {
            var response = await client.PostAsync(BaseUrl, Json("""
                {"kind":0,"channel":0,"subject":null,"body":"Body","constraints":[]}
                """));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("default");
        }
    }

    // ── Put (by id) ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Put_BindsTheIdAndTheBody()
    {
        var update = new CapturingUpdateHandler();
        var (app, client) = await StartAsync(update: update);
        await using (app)
        {
            var response = await client.PutAsync($"{BaseUrl}/{TemplateId}", Json("""
                {"subject":"Reminder: {{Title}}","body":"Due {{DueDate}}","constraints":[],"reason":"tweak"}
                """));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            update.Last.Should().NotBeNull();
            update.Last!.TemplateId.Should().Be(TemplateId);
            update.Last.Request.Body.Should().Be("Due {{DueDate}}");
            update.Last.Reason.Should().Be("tweak");
        }
    }

    [TestMethod]
    public async Task Put_Returns404WhenTheRowIsAbsent()
    {
        var (app, client) = await StartAsync(update: new MissingUpdateHandler());
        await using (app)
        {
            var response = await client.PutAsync($"{BaseUrl}/{TemplateId}", Json("""
                {"body":"Body","constraints":[]}
                """));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "the tenant query filter makes a foreign row indistinguishable from an absent one");
        }
    }

    // ── Delete / reset (by id) ─────────────────────────────────────────────

    [TestMethod]
    public async Task Delete_WithoutAReason_Returns400AndDoesNotCallTheHandler()
    {
        var delete = new CapturingDeleteHandler();
        var (app, client) = await StartAsync(delete: delete);
        await using (app)
        {
            var response = await client.DeleteAsync($"{BaseUrl}/{TemplateId}");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "a reset is audited, so the audit row must carry an operator reason "
                + "(the config-flags audited-delete precedent)");
            delete.Last.Should().BeNull();
        }
    }

    [TestMethod]
    public async Task Delete_WithABlankReason_Returns400()
    {
        var delete = new CapturingDeleteHandler();
        var (app, client) = await StartAsync(delete: delete);
        await using (app)
        {
            var response = await client.DeleteAsync($"{BaseUrl}/{TemplateId}?reason=%20%20");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            delete.Last.Should().BeNull();
        }
    }

    [TestMethod]
    public async Task Delete_WithAReason_Returns204AndPassesIdAndReasonToTheHandler()
    {
        var delete = new CapturingDeleteHandler();
        var (app, client) = await StartAsync(delete: delete);
        await using (app)
        {
            var response = await client.DeleteAsync($"{BaseUrl}/{TemplateId}?reason=reset%20after%20review");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            delete.Last.Should().NotBeNull();
            delete.Last!.TemplateId.Should().Be(TemplateId);
            delete.Last.Reason.Should().Be("reset after review");
        }
    }

    // ── Audit (stays keyed) ────────────────────────────────────────────────

    [TestMethod]
    public async Task GetAudit_Returns200AndTheEntries_ForAKindAndChannel()
    {
        var entry = new TemplateAuditEntryDto(
            Guid.NewGuid(), TemplateChangeKind.Updated,
            "old subject", "new subject", "old body", "new body",
            """[{"dimension":"GradeLevel","value":"Grade 3"}]""", "[]",
            "tweak", "test:actor", "Test Actor", DateTimeOffset.UnixEpoch);

        var (app, client) = await StartAsync(audit: [entry]);
        await using (app)
        {
            var response = await client.GetAsync($"{BaseUrl}/Publish/Email/audit");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("new body");
            body.Should().Contain("Grade 3");
        }
    }
}
