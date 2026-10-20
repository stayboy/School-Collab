using System.Net;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// bUnit tests for the grade-detail notification-templates card (spec §3.11, round
/// notification-templates-b3b): one block per authorable kind × channel, each template's constraint
/// summary, the tenant-default highlight, the "Manage templates" deep link carrying the grade filter,
/// and an inline error on a failed load (never a broken page). Read-only by contract — the card
/// renders no editing affordance.
/// </summary>
[TestClass]
public class NotificationTemplatesCardTests : BunitContext
{
    private static readonly Guid GradeId = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");

    public NotificationTemplatesCardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton<ILogger<NotificationTemplatesCard>>(
            NullLogger<NotificationTemplatesCard>.Instance);
    }

    private sealed class ScriptedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private ScriptedHandler Register(HttpStatusCode status, string body)
    {
        var handler = new ScriptedHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton(new NotificationTemplateApiClient(http));
        return handler;
    }

    /// <summary>Eight slots; Publish × Email carries a tenant default plus a constrained candidate.</summary>
    private static string EightSlots() => """
        {
          "slots": [
            {
              "kind": 0, "channel": 0, "templates": [
                { "id": "11111111-1111-1111-1111-111111111111", "subject": null, "isDefault": true, "constraints": [] },
                { "id": "22222222-2222-2222-2222-222222222222", "subject": "Grade 8 only", "isDefault": false,
                  "constraints": [{ "dimension": 0, "value": "abcdef01-2345-6789-abcd-ef0123456789", "display": "Grade 8" }] }
              ]
            },
            { "kind": 0, "channel": 1, "templates": [] },
            { "kind": 1, "channel": 0, "templates": [] },
            { "kind": 1, "channel": 1, "templates": [] },
            { "kind": 2, "channel": 0, "templates": [] },
            { "kind": 2, "channel": 1, "templates": [] },
            { "kind": 3, "channel": 0, "templates": [] },
            { "kind": 3, "channel": 1, "templates": [] }
          ]
        }
        """;

    private IRenderedComponent<NotificationTemplatesCard> RenderCard() =>
        Render<NotificationTemplatesCard>(p => p.Add(c => c.GradeLevelId, GradeId));

    [TestMethod]
    public void Renders_OneBlockPerKindAndChannel()
    {
        var handler = Register(HttpStatusCode.OK, EightSlots());

        var cut = RenderCard();

        cut.WaitForAssertion(() => cut.FindAll("[data-slot]").Should().HaveCount(8,
            "the card mirrors the eight authorable kind × channel slots"));
        cut.FindAll("[data-slot]").Select(e => e.GetAttribute("data-slot")).Should().Contain(
            ["Publish-Email", "Publish-SMS", "Reminder-Email", "Overdue-SMS"]);
        cut.Markup.Should().Contain("The built-in default applies.",
            "an empty slot says so rather than rendering a blank block");
        handler.Paths.Should().Contain($"/api/settings/notification-templates/applicable/{GradeId:D}",
            "the card's data source is the B3a applicability read for THIS grade — a wrong or missing path must fail here, not in production");
    }

    [TestMethod]
    public void ShowsTheConstraintSummary_AndHighlightsTheTenantDefault()
    {
        Register(HttpStatusCode.OK, EightSlots());

        var cut = RenderCard();

        cut.WaitForAssertion(() =>
        {
            var publishEmail = cut.Find("[data-slot='Publish-Email']");
            publishEmail.TextContent.Should().Contain("Grade level: Grade 8",
                "each candidate's constraints are displayed by their resolved label");
            publishEmail.TextContent.Should().Contain("Default",
                "the unconstrained tenant default is highlighted");
            publishEmail.TextContent.Should().Contain("No constraints",
                "the default row's empty set is spelled out");
        });
    }

    [TestMethod]
    public void ManageTemplates_DeepLinksIntoTheEditor_WithTheGradeFilter()
    {
        Register(HttpStatusCode.OK, EightSlots());

        var cut = RenderCard();

        cut.WaitForAssertion(() =>
        {
            var link = cut.Find("fluent-link");
            link.TextContent.Trim().Should().Be("Manage templates");
            link.GetAttribute("href").Should().Be($"/notification-templates?grade={GradeId:D}",
                "the editor opens pre-filtered to this grade (spec §3.11)");
        });
    }

    [TestMethod]
    public void FailedLoad_RendersAnInlineError_NeverABrokenPage()
    {
        Register(HttpStatusCode.InternalServerError, "boom");

        var cut = RenderCard();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Notification templates could not be loaded",
                "a card-level failure is shown in the card");
            cut.FindAll("[data-slot]").Should().BeEmpty("no half-rendered inventory");
        });
        cut.Markup.Should().NotContain("Something went wrong",
            "the page's ErrorBoundary must not be reached");
    }
}
