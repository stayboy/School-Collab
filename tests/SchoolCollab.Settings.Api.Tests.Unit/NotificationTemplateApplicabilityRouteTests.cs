using System.Net;
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
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetApplicableNotificationTemplates;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Tests.Unit;

/// <summary>
/// Route-resolution pin for the two same-shape template reads (round notification-templates-b3a).
/// <c>/notification-templates/applicable/{gradeLevelId}</c> matches the pre-existing
/// <c>/notification-templates/{kind}/{channel}</c> pattern as well — "applicable" and a guid are both
/// bindable as route values — so only <b>pattern precedence</b> (a literal segment outranks a
/// parameter) keeps them apart. Both halves are asserted in one test: a future route insertion or a
/// pattern change that steals either one fails here, where no handler unit test could see it.
/// </summary>
[TestClass]
public class NotificationTemplateApplicabilityRouteTests
{
    private const string BaseUrl = "/api/settings/notification-templates";

    private static readonly Guid GradeLevelId = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");

    private static TenantNotificationTemplateDto SampleDefaultRow() =>
        new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            NotificationTemplateKind.Publish, NotificationChannel.Email,
            "Grade 8 Science reminder", null, "TPL01",
            "Subject for {{Title}}", "Body for {{Title}}", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1),
            TenantId: null, OwnerName: "System");

    private sealed class ApplicableHandler : IQueryHandler<GetApplicableNotificationTemplates, ApplicableNotificationTemplatesDto>
    {
        public int Calls { get; private set; }

        public GetApplicableNotificationTemplates? Last { get; private set; }

        public Task<ApplicableNotificationTemplatesDto> HandleAsync(
            GetApplicableNotificationTemplates query, CancellationToken ct = default)
        {
            Calls++;
            Last = query;
            return Task.FromResult(new ApplicableNotificationTemplatesDto([]));
        }
    }

    private sealed class DefaultRowHandler(TenantNotificationTemplateDto? row)
        : IQueryHandler<GetTenantNotificationTemplate, TenantNotificationTemplateDto?>
    {
        public int Calls { get; private set; }

        public GetTenantNotificationTemplate? Last { get; private set; }

        public Task<TenantNotificationTemplateDto?> HandleAsync(
            GetTenantNotificationTemplate query, CancellationToken ct = default)
        {
            Calls++;
            Last = query;
            return Task.FromResult(row);
        }
    }

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        ApplicableHandler applicable, DefaultRowHandler defaultRow)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(
            (IQueryHandler<GetApplicableNotificationTemplates, ApplicableNotificationTemplatesDto>)applicable);
        builder.Services.AddSingleton(
            (IQueryHandler<GetTenantNotificationTemplate, TenantNotificationTemplateDto?>)defaultRow);

        var app = builder.Build();
        app.MapGroup("/api/settings").MapNotificationTemplateRoutes();
        await app.StartAsync();

        return (app, ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient());
    }

    [TestMethod]
    public async Task ApplicabilityAndKindChannelReads_EachReachTheirOwnHandler()
    {
        var applicable = new ApplicableHandler();
        var defaultRow = new DefaultRowHandler(SampleDefaultRow());
        var (app, client) = await StartAsync(applicable, defaultRow);
        await using (app)
        {
            // Half 1: the literal `applicable/{guid}` route.
            var applicability = await client.GetAsync($"{BaseUrl}/applicable/{GradeLevelId}");

            applicability.StatusCode.Should().Be(HttpStatusCode.OK);
            applicable.Calls.Should().Be(1,
                "a literal segment outranks the {kind} parameter, so the applicability URL must not be "
                + "bound to the default-row handler");
            applicable.Last!.GradeLevelId.Should().Be(GradeLevelId,
                "the guid route value must bind into the query, not be swallowed by {kind}/{channel}");
            defaultRow.Calls.Should().Be(0, "the default-row handler must not answer the applicability URL");
            (await applicability.Content.ReadAsStringAsync()).Should().Contain("slots");

            // Half 2: the pre-existing kind × channel default-row route.
            var kindChannel = await client.GetAsync($"{BaseUrl}/Publish/Email");

            kindChannel.StatusCode.Should().Be(HttpStatusCode.OK);
            defaultRow.Calls.Should().Be(1, "the kind × channel read must keep reaching its own handler");
            defaultRow.Last!.Kind.Should().Be(NotificationTemplateKind.Publish);
            defaultRow.Last.Channel.Should().Be(NotificationChannel.Email);
            applicable.Calls.Should().Be(1, "and must not be diverted to the applicability handler");
        }
    }
}
