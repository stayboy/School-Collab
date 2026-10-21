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
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.AdoptTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.CreateTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.PromoteTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.UpdateTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTemplateAuditEntries;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplateById;
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
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1),
            TenantId: null, OwnerName: "System");

    // ── Stubs ──────────────────────────────────────────────────────────────

    private sealed class GetAllHandler(IReadOnlyList<TenantNotificationTemplateDto> rows)
        : IQueryHandler<GetTenantNotificationTemplates, IReadOnlyList<TenantNotificationTemplateDto>>
    {
        public GetTenantNotificationTemplates? Last { get; private set; }

        public Task<IReadOnlyList<TenantNotificationTemplateDto>> HandleAsync(
            GetTenantNotificationTemplates query, CancellationToken ct = default)
        {
            Last = query;
            return Task.FromResult(rows);
        }
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

    /// <summary>Stub for the by-id read (redesign spec §5), mirroring <see cref="GetOneHandler"/>.</summary>
    private sealed class GetByIdHandler(TenantNotificationTemplateDto? result)
        : IQueryHandler<GetTenantNotificationTemplateById, TenantNotificationTemplateDto?>
    {
        public GetTenantNotificationTemplateById? Last { get; private set; }

        public Task<TenantNotificationTemplateDto?> HandleAsync(
            GetTenantNotificationTemplateById query, CancellationToken ct = default)
        {
            Last = query;
            return Task.FromResult(result);
        }
    }

    /// <summary>Reproduces a handler rejecting the request so the route's 400 arm is reached.</summary>
    private sealed class ChannelGuardingCreateHandler
        : ICommandHandler<CreateTenantNotificationTemplate, TenantNotificationTemplateDto>
    {
        public Task<TenantNotificationTemplateDto> HandleAsync(
            CreateTenantNotificationTemplate command, CancellationToken ct = default)
        {
            if (command.Request.Channel == NotificationChannel.WhatsApp)
                throw new ArgumentOutOfRangeException(nameof(command), "This channel is not accepted here");

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

    /// <summary>Reproduces the scope guard so the route's 403 arm is reached (redesign spec §6).</summary>
    private sealed class ScopeGuardingUpdateHandler : ICommandHandler<UpdateTenantNotificationTemplate, TenantNotificationTemplateDto>
    {
        public Task<TenantNotificationTemplateDto> HandleAsync(
            UpdateTenantNotificationTemplate command, CancellationToken ct = default) =>
            throw new TenantNotificationTemplateScopeException("system blueprint row, tenant session");
    }

    private sealed class ScopeGuardingDeleteHandler : ICommandHandler<DeleteTenantNotificationTemplate>
    {
        public Task HandleAsync(DeleteTenantNotificationTemplate command, CancellationToken ct = default) =>
            throw new TenantNotificationTemplateScopeException("system blueprint row, tenant session");
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
        public GetTemplateAuditEntries? Last { get; private set; }

        public Task<IReadOnlyList<TemplateAuditEntryDto>> HandleAsync(
            GetTemplateAuditEntries query, CancellationToken ct = default)
        {
            Last = query;
            return Task.FromResult(rows);
        }
    }

    /// <summary>
    /// Adopt/promote stub (redesign spec §3.8): answers with a row, or throws the typed refusal the
    /// route must translate (403 scope, 409 identical set, 404 unknown id).
    /// </summary>
    private sealed class CopyStub<TCommand>(TenantNotificationTemplateDto? result, Exception? error)
        : ICommandHandler<TCommand, TenantNotificationTemplateDto>
        where TCommand : class, ICommand
    {
        public TCommand? Last { get; private set; }

        public Task<TenantNotificationTemplateDto> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            Last = command;
            if (error is not null)
                throw error;

            return Task.FromResult(result!);
        }
    }

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        IReadOnlyList<TenantNotificationTemplateDto>? all = null,
        TenantNotificationTemplateDto? single = null,
        ICommandHandler<CreateTenantNotificationTemplate, TenantNotificationTemplateDto>? create = null,
        ICommandHandler<UpdateTenantNotificationTemplate, TenantNotificationTemplateDto>? update = null,
        ICommandHandler<DeleteTenantNotificationTemplate>? delete = null,
        IReadOnlyList<TemplateAuditEntryDto>? audit = null,
        StubAuditHandler? auditSpy = null,
        GetOneHandler? getOneSpy = null,
        GetByIdHandler? getByIdSpy = null,
        GetAllHandler? getAllSpy = null,
        ICommandHandler<AdoptTenantNotificationTemplate, TenantNotificationTemplateDto>? adopt = null,
        ICommandHandler<PromoteTenantNotificationTemplate, TenantNotificationTemplateDto>? promote = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton<IQueryHandler<GetTenantNotificationTemplates, IReadOnlyList<TenantNotificationTemplateDto>>>(
            getAllSpy ?? new GetAllHandler(all ?? []));
        builder.Services.AddSingleton<IQueryHandler<GetTenantNotificationTemplate, TenantNotificationTemplateDto?>>(
            getOneSpy ?? new GetOneHandler(single));
        builder.Services.AddSingleton<IQueryHandler<GetTenantNotificationTemplateById, TenantNotificationTemplateDto?>>(
            getByIdSpy ?? new GetByIdHandler(null));
        builder.Services.AddSingleton(create ?? new CapturingCreateHandler());
        builder.Services.AddSingleton(update ?? new CapturingUpdateHandler());
        builder.Services.AddSingleton(delete ?? new CapturingDeleteHandler());
        builder.Services.AddSingleton<ICommandHandler<AdoptTenantNotificationTemplate, TenantNotificationTemplateDto>>(
            adopt ?? new CopyStub<AdoptTenantNotificationTemplate>(SampleDto(), null));
        builder.Services.AddSingleton<ICommandHandler<PromoteTenantNotificationTemplate, TenantNotificationTemplateDto>>(
            promote ?? new CopyStub<PromoteTenantNotificationTemplate>(SampleDto(), null));
        builder.Services.AddSingleton<IQueryHandler<GetTemplateAuditEntries, IReadOnlyList<TemplateAuditEntryDto>>>(
            auditSpy ?? new StubAuditHandler(audit ?? []));

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

    // ── Get by id (redesign spec §5) ───────────────────────────────────────

    [TestMethod]
    public async Task GetById_Returns200AndTheRow()
    {
        var spy = new GetByIdHandler(SampleDto());
        var (app, client) = await StartAsync(getByIdSpy: spy);
        await using (app)
        {
            var response = await client.GetAsync($"{BaseUrl}/{TemplateId:D}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Body for {{Title}}");
            spy.Last.Should().NotBeNull();
            spy.Last!.Id.Should().Be(TemplateId);
        }
    }

    [TestMethod]
    public async Task GetById_Returns404WhenTheRowIsNotVisible()
    {
        var (app, client) = await StartAsync(getByIdSpy: new GetByIdHandler(null));
        await using (app)
        {
            var response = await client.GetAsync($"{BaseUrl}/{TemplateId:D}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
        // WhatsApp is authorable since the redesign (spec §3.10), so this pins the route's generic 400
        // arm only: any ArgumentException the handler raises maps to 400, never a 500.
        var (app, client) = await StartAsync(create: new ChannelGuardingCreateHandler());
        await using (app)
        {
            // channel 2 = WhatsApp; the stub rejects it, which is what this test pins.
            var response = await client.PostAsync(BaseUrl, Json("""
                {"kind":0,"channel":2,"subject":null,"body":"Publish: {{Title}}","constraints":[]}
                """));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("not accepted here",
                "the 400 must come from the handler's ArgumentException, not from body binding");
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

    // ── Audit (merged per slot, optionally narrowed by template) ───────────

    [TestMethod]
    public async Task GetAudit_PassesTheTemplateIdFilterThrough()
    {
        var audit = new StubAuditHandler([]);
        var (app, client) = await StartAsync(auditSpy: audit);
        await using (app)
        {
            var response = await client.GetAsync($"{BaseUrl}/Publish/Email/audit?templateId={TemplateId:D}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            audit.Last.Should().NotBeNull();
            audit.Last!.TemplateId.Should().Be(TemplateId,
                "the detail page narrows the stream to its own row (redesign spec §3.12)");
        }
    }

    [TestMethod]
    public async Task GetAudit_Returns200AndTheEntries_ForAKindAndChannel()
    {
        var entry = new TemplateAuditEntryDto(
            Guid.NewGuid(), TemplateId, TemplateChangeKind.Updated,
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

    // ── Ownership (redesign spec §3.6-§3.8, §6) ───────────────────────────

    [TestMethod]
    public async Task GetAll_BindsIncludeTenantOwnedFromTheQueryString()
    {
        var spy = new GetAllHandler([SampleDto()]);
        var (app, client) = await StartAsync(getAllSpy: spy);
        await using (app)
        {
            await client.GetAsync(BaseUrl);
            spy.Last!.IncludeTenantOwned.Should().BeFalse("the flag defaults off");

            await client.GetAsync($"{BaseUrl}?includeTenantOwned=true");
            spy.Last!.IncludeTenantOwned.Should().BeTrue(
                "the promote picker's source rides the query string (spec §3.6)");
        }
    }

    [TestMethod]
    public async Task Put_OnARowOutsideTheCallersScope_Is403()
    {
        var (app, client) = await StartAsync(update: new ScopeGuardingUpdateHandler());
        await using (app)
        {
            var response = await client.PutAsync(
                $"{BaseUrl}/{TemplateId:D}",
                Json("""{"subject":null,"body":"x","constraints":[]}"""));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "a visible row must not be silently rewritten outside its scope");
        }
    }

    [TestMethod]
    public async Task Adopt_Returns200_TheCopy_AndTheReason()
    {
        var stub = new CopyStub<AdoptTenantNotificationTemplate>(SampleDto(), null);
        var (app, client) = await StartAsync(adopt: stub);
        await using (app)
        {
            var response = await client.PostAsync($"{BaseUrl}/{TemplateId:D}/adopt?reason=try%20it", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            stub.Last!.TemplateId.Should().Be(TemplateId);
            stub.Last.Reason.Should().Be("try it");
        }
    }

    [TestMethod]
    public async Task Adopt_FromSystemScope_Is403_And_AnIdenticalSetIs409()
    {
        var forbidden = new CopyStub<AdoptTenantNotificationTemplate>(
            null, new TenantNotificationTemplateScopeException("needs a tenant"));
        var (app, client) = await StartAsync(adopt: forbidden);
        await using (app)
        {
            (await client.PostAsync($"{BaseUrl}/{TemplateId:D}/adopt", null))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var conflict = new CopyStub<AdoptTenantNotificationTemplate>(
            null, new TenantNotificationTemplateConflictException("already adopted"));
        var (app2, client2) = await StartAsync(adopt: conflict);
        await using (app2)
        {
            var response = await client2.PostAsync($"{BaseUrl}/{TemplateId:D}/adopt", null);

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("already adopted");
        }
    }

    [TestMethod]
    public async Task Adopt_AnUnknownId_Is404()
    {
        var stub = new CopyStub<AdoptTenantNotificationTemplate>(
            null, new TenantNotificationTemplateNotFoundException(TemplateId));
        var (app, client) = await StartAsync(adopt: stub);
        await using (app)
        {
            (await client.PostAsync($"{BaseUrl}/{TemplateId:D}/adopt", null))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [TestMethod]
    public async Task Promote_Returns200_TheBlueprintCopy()
    {
        var stub = new CopyStub<PromoteTenantNotificationTemplate>(SampleDto(), null);
        var (app, client) = await StartAsync(promote: stub);
        await using (app)
        {
            var response = await client.PostAsync($"{BaseUrl}/{TemplateId:D}/promote", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            stub.Last!.TemplateId.Should().Be(TemplateId);
        }
    }

    [TestMethod]
    public async Task Promote_FromATenantSession_Is403()
    {
        var stub = new CopyStub<PromoteTenantNotificationTemplate>(
            null, new TenantNotificationTemplateScopeException("system scope only"));
        var (app, client) = await StartAsync(promote: stub);
        await using (app)
        {
            (await client.PostAsync($"{BaseUrl}/{TemplateId:D}/promote", null))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [TestMethod]
    public async Task Promote_AnIdenticalSystemSet_Is409()
    {
        var stub = new CopyStub<PromoteTenantNotificationTemplate>(
            null, new TenantNotificationTemplateConflictException("already in the blueprint"));
        var (app, client) = await StartAsync(promote: stub);
        await using (app)
        {
            (await client.PostAsync($"{BaseUrl}/{TemplateId:D}/promote", null))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }

    [TestMethod]
    public async Task Delete_OnARowOutsideTheCallersScope_Is403()
    {
        var (app, client) = await StartAsync(delete: new ScopeGuardingDeleteHandler());
        await using (app)
        {
            (await client.DeleteAsync($"{BaseUrl}/{TemplateId:D}?reason=try"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }
}
