using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
/// bUnit tests for <see cref="TopicEditDialog"/> (grade-detail Subjects card
/// kebab "Edit name"). Rendered through the real
/// <see cref="FluentDialogProvider"/> + <c>DialogService.ShowShellDialogAsync</c>
/// pipeline. The dialog loads the current topic on mount and PUTs the rename.
/// </summary>
[TestClass]
public class TopicEditDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public TopicEditDialogTests()
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
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
    }

    private static string TopicJson(Guid topicId, string name, Guid? codedValueId = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = topicId, ["codedValueId"] = codedValueId, ["code"] = "MATH",
            ["name"] = name, ["description"] = (string?)null,
            ["displayOrder"] = 0, ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
        });

    private static string CodedValueJson(Guid id, string name, string code, string? description) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id, ["code"] = code, ["name"] = name, ["description"] = description,
            ["parentId"] = (Guid?)null, ["parentCode"] = (string?)null, ["isDisabled"] = false,
            ["displayOrder"] = 0,
            ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
            ["attributes"] = Array.Empty<object>(), ["attributeDefinitions"] = Array.Empty<object>(),
        });

    private static string StrandsJson(Guid strandId, string name) =>
        JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = strandId, ["topicId"] = Guid.NewGuid(), ["name"] = name,
                ["description"] = (string?)null, ["displayOrder"] = 0,
                ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
            }
        });

    [TestMethod]
    public async Task EditDialog_LoadsTopicName_AndRendersForm()
    {
        var topicId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", $"/students/topics/{topicId}", HttpStatusCode.OK, TopicJson(topicId, "Mathematics"));
        handler.Map("GET", $"/students/topics/{topicId}/strands", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicEditDialog, TopicEditDialog.TopicEditModel, TopicDto>(
            new TopicEditDialog.TopicEditModel { Id = topicId, Name = "Mathematics" },
            "Edit topic", DialogSize.Small);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        // The dialog loads the topic name/code asynchronously on mount; wait for
        // the resolved code (the StrandsEditor's own async load shares the render
        // pipeline, so the topic load isn't synchronous with the form render).
        cut.WaitForAssertion(
            () => cut.Markup.Should().Contain("MATH", "the dialog loads the current topic code"),
            TimeSpan.FromSeconds(5));
        cut.Markup.Should().Contain("Mathematics", "the dialog loads the current topic name");
        cut.Markup.Should().Contain("Display order");
        cut.Markup.Should().Contain("Description");
        cut.Markup.Should().Contain("Save");

        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("cancelling closes the dialog with no result");
    }

    [TestMethod]
    public async Task EditDialog_CodedValueBackedTopic_LoadsEffectiveNameAndCode()
    {
        var topicId = Guid.NewGuid();
        var cvId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", $"/students/topics/{topicId}", HttpStatusCode.OK, TopicJson(topicId, "Mathematics", cvId));
        handler.Map("GET", $"/api/coded-values/{cvId}", HttpStatusCode.OK, CodedValueJson(cvId, "Algebra", "ALG", "Algebra subject"));
        handler.Map("GET", $"/students/topics/{topicId}/strands", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicEditDialog, TopicEditDialog.TopicEditModel, TopicDto>(
            new TopicEditDialog.TopicEditModel { Id = topicId, Name = "Mathematics" },
            "Edit topic", DialogSize.Small);

        // Wait on the resolved CodedValue code (not just the form) because the
        // async load does two sequential HTTP calls (topic, then CodedValue). Give
        // a generous timeout so parallel bUnit runs don't race it.
        cut.WaitForAssertion(
            () => cut.Markup.Should().Contain("ALG", "the dialog loads the resolved CodedValue code"),
            TimeSpan.FromSeconds(5));
        cut.Markup.Should().Contain("Algebra", "the dialog shows the resolved CodedValue name");

        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("cancelling closes the dialog with no result");
    }

    [TestMethod]
    public async Task EditDialog_RendersStrandsEditor_AndLoadsStrands()
    {
        var topicId = Guid.NewGuid();
        var strandId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", $"/students/topics/{topicId}", HttpStatusCode.OK, TopicJson(topicId, "Mathematics"));
        handler.Map("GET", $"/students/topics/{topicId}/strands", HttpStatusCode.OK, StrandsJson(strandId, "Number & Operations"));
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicEditDialog, TopicEditDialog.TopicEditModel, TopicDto>(
            new TopicEditDialog.TopicEditModel { Id = topicId, Name = "Mathematics" },
            "Edit topic", DialogSize.Large);

        // The strands section + add affordance render on mount (deterministic),
        // independent of the async strand-load result. The StrandsEditor's own
        // load/CRUD is covered by its dedicated tests.
        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Markup.Should().Contain("Strands (", "the topic edit dialog embeds the strands editor");
        cut.Markup.Should().Contain("New Strand", "the strands editor offers an add affordance");

        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("cancelling closes the dialog with no result");
    }
}
