using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Admin.Components.Students;
using SchoolCollab.Students.Admin.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit smoke tests for <see cref="SubjectEditDialog"/> + <see cref="SubjectCreateDialog"/>.
/// Mounts both through the real <see cref="FluentDialogProvider"/> +
/// <c>DialogService.ShowDialogAsync</c> pipeline. The same plan-aligned
/// pattern as <c>GradeLevelEditDialogTests</c> / <c>GradeLevelCreateDialogTests</c>;
/// deeper submit-pipeline assertions are deferred to integration tests
/// because the bUnit FluentListbox / CodedValueDropdown two-way bindings
/// cannot be reliably driven from headless tests.
/// </summary>
[TestClass]
public class SubjectDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public SubjectDialogTests()
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

        public ScriptedHandler Map(string url, HttpStatusCode status, string body)
            => Map("ANY", url, status, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));
            var url = request.RequestUri.PathAndQuery;
            (HttpStatusCode Status, string Body)? found = null;
            if (_responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var exact))
                found = exact;
            else
            {
                foreach (var kv in _responses)
                {
                    if (kv.Key.Method != "ANY") continue;
                    if (url.Equals(kv.Key.Url, StringComparison.OrdinalIgnoreCase) ||
                        url.StartsWith(kv.Key.Url, StringComparison.OrdinalIgnoreCase))
                    {
                        found = kv.Value;
                        break;
                    }
                }
            }
            if (found is { } hit)
            {
                return new HttpResponseMessage(hit.Status)
                {
                    Content = new StringContent(hit.Body, System.Text.Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {request.Method.Method} {url}", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private ScriptedHandler Register()
    {
        var handler = new ScriptedHandler();
        // CodedValueDropdown parent lookups - empty catalog.
        handler.Map("/api/coded-values/by-parent", System.Net.HttpStatusCode.OK, "[]");
        // No API calls expected on the smoke path; mount + cancel only.
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        return handler;
    }

    private IRenderedComponent<FluentDialogProvider> RenderProvider() => Render<FluentDialogProvider>();

    [TestMethod]
    public async Task Edit_Dialog_Renders_Code_And_Name_With_Pencil()
    {
        Register();
        var cut = RenderProvider();

        var model = new SubjectEditDialog.SubjectEditModel
        {
            CodedValueId = Guid.NewGuid(),
            CurrentCode = "MATH",
            CurrentName = "Mathematics",
        };

        var task = DialogService.ShowShellDialogAsync<
            SubjectEditDialog,
            SubjectEditDialog.SubjectEditModel,
            CodedValueDto>(
            model, "Edit subject - MATH", DialogSize.Small);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Markup.Should().Contain("MATH");
        cut.Markup.Should().Contain("Mathematics");
        cut.Markup.Should().Contain("Override the tenant display name",
            "the pencil icon button is rendered with a tooltip");

        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        var result = await task;
        result.Should().BeNull("cancelling closes the dialog with no result");
    }

    [TestMethod]
    public async Task Create_Dialog_Renders_Subject_Picker_And_NoGradeWarning_When_NoContext()
    {
        Register();
        var cut = RenderProvider();

        var model = new SubjectCreateDialog.SubjectCreateModel
        {
            GradeLevelId = null,
        };

        var task = DialogService.ShowShellDialogAsync<
            SubjectCreateDialog,
            SubjectCreateDialog.SubjectCreateModel,
            SubjectDto>(
            model, "New subject", DialogSize.Small);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Markup.Should().Contain("Subject");
        cut.Markup.Should().Contain("No grade level selected",
            "without a grade-level filter, the dialog shows the 'pick a grade first' hint");

        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        var result = await task;
        result.Should().BeNull();
    }
}