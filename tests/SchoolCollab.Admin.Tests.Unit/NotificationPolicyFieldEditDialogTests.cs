using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.DTOs;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for <see cref="NotificationPolicyFieldEditDialog"/> - the dialog
/// opened from the grade-detail "Notification &amp; Delivery" grid "Edit" action.
/// Rendered through the real <see cref="FluentDialogProvider"/> +
/// <c>DialogService.ShowShellDialogAsync</c> pipeline. Exercises the side-by-side
/// Global-settings / This-grade panels and that Save writes each scope only when
/// its value changed.
/// </summary>
[TestClass]
public class NotificationPolicyFieldEditDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public NotificationPolicyFieldEditDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Calls = new();
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));
            var key = (request.Method.Method.ToUpperInvariant(), request.RequestUri.PathAndQuery);
            if (_responses.TryGetValue(key, out var hit))
                return new HttpResponseMessage(hit.Status) { Content = new StringContent(hit.Body, Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {request.Method.Method} {request.RequestUri.PathAndQuery}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private ScriptedHandler Register(Guid gradeId)
    {
        var handler = new ScriptedHandler();
        handler.Map("PUT", "/api/settings/notification-policy", HttpStatusCode.OK, "");
        handler.Map("PUT", $"/students/grade-levels/{gradeId}/notification-policy", HttpStatusCode.OK, "");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton(new NotificationPolicyApiClient(http));
        var codedValues = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues));
        return handler;
    }

    private static TenantNotificationPolicyDto Tenant(
        NotificationChannel[]? preferred = null,
        NotificationChannel[]? blocked = null,
        int? maxNotifications = null,
        int? linkValidityDays = null,
        TimeOnly? sendoutTimeOfDay = null) =>
        new(
            Guid.NewGuid(),
            preferred ?? [],
            blocked ?? [],
            maxNotifications,
            null,
            null,
            linkValidityDays,
            sendoutTimeOfDay,
            null,
            System.DateTimeOffset.UnixEpoch);

    private static GradeNotificationPolicyDto Grade(
        Guid gradeId,
        NotificationChannel[]? preferred = null,
        NotificationChannel[]? blocked = null,
        int? maxNotifications = null,
        int? sendoutTimeOfDay = null) =>
        new(
            gradeId,
            preferred,
            blocked ?? [],
            maxNotifications,
            null,
            null,
            null,
            sendoutTimeOfDay is int h ? new TimeOnly(h, 0) : null,
            null,
            System.DateTimeOffset.UnixEpoch);

    [TestMethod]
    public async Task Dialog_Shows_Both_Scope_Panels_SideBySide()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new NotificationPolicyFieldEditDialog.EditModel(
            "BlockedChannels", "Blocked channels", NotificationPolicyFieldEditDialog.FieldKind.Channels,
            gradeId,
            Tenant(preferred: [NotificationChannel.Email]),
            Grade(gradeId, blocked: []));

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<NotificationPolicyFieldEditDialog,
            NotificationPolicyFieldEditDialog.EditModel,
            NotificationPolicyFieldEditDialog.EditResult>(model, "Edit Blocked channels", DialogSize.Large);

        cut.WaitForAssertion(() => cut.FindAll(".split__panel").Should().HaveCount(2));
        cut.Markup.Should().Contain("Global settings", "the global-settings panel header renders");
        cut.Markup.Should().Contain("This grade", "the per-grade panel header renders");
        cut.Find("div[role=separator]").Should().NotBeNull("the panels are separated by a vertical bar");
        cut.FindAll(".split__panel--global .channel-option").Should().NotBeEmpty();
        cut.FindAll(".split__panel--grade .channel-option").Should().NotBeEmpty();

        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Cancel").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("cancelling closes the dialog after inspecting the panels");
    }

    [TestMethod]
    public async Task Dialog_ChangesOnlyGradeScope_WritesGradeEndpoint_Only()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new NotificationPolicyFieldEditDialog.EditModel(
            "BlockedChannels", "Blocked channels", NotificationPolicyFieldEditDialog.FieldKind.Channels,
            gradeId,
            Tenant(preferred: [NotificationChannel.Email]),
            Grade(gradeId, blocked: []));

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<NotificationPolicyFieldEditDialog,
            NotificationPolicyFieldEditDialog.EditModel,
            NotificationPolicyFieldEditDialog.EditResult>(model, "Edit Blocked channels", DialogSize.Large);

        cut.WaitForAssertion(() => cut.FindAll(".split__panel--grade .channel-option").Should().NotBeEmpty());
        // Toggle SMS into the GRADE-side blocked list only.
        cut.FindAll(".split__panel--grade .channel-option").Single(l => l.TextContent.Contains("SMS"))
            .QuerySelector("input[type=checkbox]")!.Change(true);
        cut.Find("form").Submit();

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();

        var put = handler.Calls.Should().Contain(c => c.Method == "PUT" && c.Url.Contains("grade-levels")).Which;
        put.Body.Should().Contain("\"blockedChannels\":[1]", "SMS (enum 1) is set on the per-grade override");
        handler.Calls.Should().NotContain(
            c => c.Method == "PUT" && c.Url == "/api/settings/notification-policy",
            "the unchanged global scope is not written");
    }

    [TestMethod]
    public async Task Dialog_ChangesOnlyGlobalScope_WritesSettingsEndpoint_Only()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new NotificationPolicyFieldEditDialog.EditModel(
            "PreferredChannelOrder", "Preferred channels", NotificationPolicyFieldEditDialog.FieldKind.Channels,
            gradeId,
            Tenant(preferred: [NotificationChannel.Email]),
            Grade(gradeId));

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<NotificationPolicyFieldEditDialog,
            NotificationPolicyFieldEditDialog.EditModel,
            NotificationPolicyFieldEditDialog.EditResult>(model, "Edit Preferred channels", DialogSize.Large);

        cut.WaitForAssertion(() => cut.FindAll(".split__panel--global .channel-option").Should().NotBeEmpty());
        // Add SMS to the GLOBAL-side preferred list only.
        cut.FindAll(".split__panel--global .channel-option").Single(l => l.TextContent.Contains("SMS"))
            .QuerySelector("input[type=checkbox]")!.Change(true);
        cut.Find("form").Submit();

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();

        var put = handler.Calls.Should().Contain(c => c.Method == "PUT" && c.Url == "/api/settings/notification-policy").Which;
        // Email (0) retained from the tenant default + SMS (1) added.
        put.Body.Should().Contain("\"preferredChannelOrder\":[0,1]");
        handler.Calls.Should().NotContain(
            c => c.Method == "PUT" && c.Url.Contains("grade-levels"),
            "an untouched grade scope (no override) is not created");
    }

    [TestMethod]
    public async Task Dialog_ChangesBothScopes_WritesBothEndpoints()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new NotificationPolicyFieldEditDialog.EditModel(
            "PreferredChannelOrder", "Preferred channels", NotificationPolicyFieldEditDialog.FieldKind.Channels,
            gradeId,
            Tenant(preferred: [NotificationChannel.Email]),
            Grade(gradeId));

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<NotificationPolicyFieldEditDialog,
            NotificationPolicyFieldEditDialog.EditModel,
            NotificationPolicyFieldEditDialog.EditResult>(model, "Edit Preferred channels", DialogSize.Large);

        cut.WaitForAssertion(() => cut.FindAll(".channel-option").Should().NotBeEmpty());
        // Global: Email (kept) + SMS. Grade (no override yet): WhatsApp only.
        cut.FindAll(".split__panel--global .channel-option").Single(l => l.TextContent.Contains("SMS"))
            .QuerySelector("input[type=checkbox]")!.Change(true);
        cut.FindAll(".split__panel--grade .channel-option").Single(l => l.TextContent.Contains("WhatsApp"))
            .QuerySelector("input[type=checkbox]")!.Change(true);
        cut.Find("form").Submit();

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();

        var settingsPut = handler.Calls.Should().Contain(c => c.Method == "PUT" && c.Url == "/api/settings/notification-policy").Which;
        settingsPut.Body.Should().Contain("\"preferredChannelOrder\":[0,1]", "global preferred = Email + SMS");

        var gradePut = handler.Calls.Should().Contain(c => c.Method == "PUT" && c.Url.Contains("grade-levels")).Which;
        gradePut.Body.Should().Contain("\"preferredChannelOrder\":[2]", "grade preferred = WhatsApp (enum 2)");
    }

    [TestMethod]
    public async Task Dialog_TimeField_GradeScope_ParsesAndSendsTime()
    {
        var gradeId = Guid.NewGuid();
        var handler = Register(gradeId);
        var model = new NotificationPolicyFieldEditDialog.EditModel(
            "SendoutTimeOfDay", "Sendout time of day", NotificationPolicyFieldEditDialog.FieldKind.Time,
            gradeId,
            Tenant(sendoutTimeOfDay: null),
            Grade(gradeId));

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<NotificationPolicyFieldEditDialog,
            NotificationPolicyFieldEditDialog.EditModel,
            NotificationPolicyFieldEditDialog.EditResult>(model, "Edit Sendout time of day", DialogSize.Large);

        cut.WaitForAssertion(() => cut.FindAll(".split__panel--grade input[type=time]").Should().NotBeEmpty());
        cut.Find(".split__panel--grade input[type=time]").Change("09:30");
        cut.Find("form").Submit();

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();

        var put = handler.Calls.Should().Contain(c => c.Method == "PUT" && c.Url.Contains("grade-levels")).Which;
        put.Body.Should().Contain("\"sendoutTimeOfDay\":\"09:30:00\"");
        handler.Calls.Should().NotContain(
            c => c.Method == "PUT" && c.Url == "/api/settings/notification-policy",
            "the unchanged global scope is not written");
    }
}
