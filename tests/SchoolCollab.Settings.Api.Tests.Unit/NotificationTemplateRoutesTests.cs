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
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.UpsertTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTemplateAuditEntries;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplates;
using SchoolCollab.Settings.Core.Domain;
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
/// 404/400 at runtime with every unit test still green), the 204-vs-200 get-one contract,
/// the 400 arm for a rejected channel, and the required-reason 400 on reset.
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
            DateTimeOffset.UnixEpoch);

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

    private sealed class CapturingUpsertHandler : ICommandHandler<UpsertTenantNotificationTemplate, TenantNotificationTemplateDto>
    {
        public UpsertTenantNotificationTemplate? Last { get; private set; }

        public Task<TenantNotificationTemplateDto> HandleAsync(
            UpsertTenantNotificationTemplate command, CancellationToken ct = default)
        {
            Last = command;
            return Task.FromResult(SampleDto(command.Kind, command.Channel));
        }
    }

    /// <summary>Reproduces the domain's WhatsApp guard so the route's 400 arm is reached.</summary>
    private sealed class ChannelGuardingUpsertHandler : ICommandHandler<UpsertTenantNotificationTemplate, TenantNotificationTemplateDto>
    {
        public Task<TenantNotificationTemplateDto> HandleAsync(
            UpsertTenantNotificationTemplate command, CancellationToken ct = default)
        {
            if (command.Channel == NotificationChannel.WhatsApp)
                throw new ArgumentOutOfRangeException(nameof(command), "WhatsApp is not a template channel");

            return Task.FromResult(SampleDto(command.Kind, command.Channel));
        }
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
        ICommandHandler<UpsertTenantNotificationTemplate, TenantNotificationTemplateDto>? upsert = null,
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
        builder.Services.AddSingleton(upsert ?? new CapturingUpsertHandler());
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
    public async Task GetOne_Returns204WhenNoTemplateIsConfigured()
    {
        // Spec §5: "204 = unset (built-in default applies)".
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

    // ── Put ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Put_BindsTheRouteEnumsAndTheBody()
    {
        var upsert = new CapturingUpsertHandler();
        var (app, client) = await StartAsync(upsert: upsert);
        await using (app)
        {
            var response = await client.PutAsync($"{BaseUrl}/Reminder/Email", Json("""
                {"subject":"Reminder: {{Title}}","body":"Due {{DueDate}}","constraints":[],"reason":"tweak"}
                """));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            upsert.Last.Should().NotBeNull();
            upsert.Last!.Kind.Should().Be(NotificationTemplateKind.Reminder);
            upsert.Last.Channel.Should().Be(NotificationChannel.Email);
            upsert.Last.Request.Body.Should().Be("Due {{DueDate}}");
            upsert.Last.Request.Reason.Should().Be("tweak");
        }
    }

    [TestMethod]
    public async Task Put_Returns400WhenTheHandlerRejectsTheChannel()
    {
        // The domain rejects WhatsApp (spec §2 routes WhatsApp at the SMS body); the
        // route catches ArgumentException — of which ArgumentOutOfRangeException is one.
        var (app, client) = await StartAsync(upsert: new ChannelGuardingUpsertHandler());
        await using (app)
        {
            var response = await client.PutAsync($"{BaseUrl}/Publish/WhatsApp", Json("""
                {"body":"Publish: {{Title}}","constraints":[]}
                """));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    // ── Delete / reset ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task Delete_WithoutAReason_Returns400AndDoesNotCallTheHandler()
    {
        var delete = new CapturingDeleteHandler();
        var (app, client) = await StartAsync(delete: delete);
        await using (app)
        {
            var response = await client.DeleteAsync($"{BaseUrl}/Publish/Email");

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
            var response = await client.DeleteAsync($"{BaseUrl}/Publish/Email?reason=%20%20");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            delete.Last.Should().BeNull();
        }
    }

    [TestMethod]
    public async Task Delete_WithAReason_Returns204AndPassesItToTheHandler()
    {
        var delete = new CapturingDeleteHandler();
        var (app, client) = await StartAsync(delete: delete);
        await using (app)
        {
            var response = await client.DeleteAsync($"{BaseUrl}/Completion/SMS?reason=reset%20after%20review");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            delete.Last.Should().NotBeNull();
            delete.Last!.Kind.Should().Be(NotificationTemplateKind.Completion);
            delete.Last.Channel.Should().Be(NotificationChannel.SMS);
            delete.Last.Reason.Should().Be("reset after review");
        }
    }

    // ── Audit ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetAudit_Returns200AndTheEntries()
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
