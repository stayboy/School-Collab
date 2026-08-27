using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using TopicDto = SchoolCollab.Students.Core.DTOs.TopicDto;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for <see cref="TopicCreateDialog"/> (grade-detail Subjects card
/// Add button). Rendered through the real <see cref="FluentDialogProvider"/> +
/// <c>DialogService.ShowShellDialogAsync</c> pipeline. The dialog creates a
/// brand-new topic (displayed as a subject) wired to a grade.
/// </summary>
[TestClass]
public class TopicCreateDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public TopicCreateDialogTests()
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

            var url = request.RequestUri.PathAndQuery;
            if (_responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var exact))
                return new HttpResponseMessage(exact.Status) { Content = new StringContent(exact.Body, Encoding.UTF8, "application/json") };

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"Unexpected {url}", Encoding.UTF8, "application/json") };
        }
    }

    private void Register(ScriptedHandler handler)
    {
        // The dialog's OnInitializedAsync loads activity groups and periods to
        // populate the owner/period pickers. Map them to empty so the dialog
        // renders in the test. GetActiveAcademicYearAsync returns null on 404.
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
        Services.AddSingleton(new EntityCodeRulesApiClient(http));
    }

    private static TopicCreateDialog.TopicCreateModel CreateModel() =>
        new() { GradeLevelId = Guid.NewGuid() };

    /// <summary>
    /// The Create submit button text must come from the dialog's
    /// <c>SubmitText</c> override ("Create") — the footer must reference
    /// <c>@SubmitText</c> rather than a hardcoded literal.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_ShowsSubmitText_Create()
    {
        Register(new ScriptedHandler());

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            CreateModel(), "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        // The submit button text is "Create" (from the SubmitText override).
        var submit = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Create"));
        submit.Should().NotBeNull("the Submit button text MUST be \"Create\" (the dialog overrides SubmitText)");
        submit.TextContent.Should().NotBe("Save", "the dialog must override the default DialogShellBase SubmitText of \"Save\"");

        // Cleanup: close the dialog from display mode.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// The create dialog toggles its submit like the edit dialog: display mode
    /// shows a server "Create", edit mode shows a LOCAL "Update Fields" (no
    /// server submit).
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_EditMode_TogglesToUpdateFields_LocalAction()
    {
        Register(new ScriptedHandler());

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            CreateModel(), "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        // Display mode: the submit is the server "Create".
        var create = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Create"));
        create.GetAttribute("type").Should().Be("submit", "display mode shows the server Create action");
        cut.Markup.Should().NotContain("Update Fields", "display mode does not show Update Fields");

        // Switch to edit mode.
        cut.Find("fluent-button[aria-label='Edit topic fields']").Click();

        // Edit mode: the submit becomes the LOCAL "Update Fields" (a plain
        // button, not a form submit) — the server "Create" is gone.
        // WaitForAssertion: the CodedValueDropdown loads coded values
        // asynchronously, and the re-render from StartEditing races with that
        // pending load. Asserting immediately was flaky in CI (the read-back
        // markup could still be the display-mode "Create" snapshot).
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Update Fields",
            "edit mode shows the local Update Fields action"));
        cut.Markup.Should().NotContain("Create", "edit mode replaces the server Create with Update Fields");
        var update = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Update Fields"));
        update.GetAttribute("type").Should().NotBe("submit", "Update Fields is a local action, not a server submit");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }
}
