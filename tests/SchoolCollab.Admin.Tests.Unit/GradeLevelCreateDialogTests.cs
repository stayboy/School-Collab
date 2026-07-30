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
using SchoolCollab.Students.Admin.Components.Students;
using SchoolCollab.Students.Admin.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for <see cref="GradeLevelCreateDialog"/> mounted through the
/// real <see cref="FluentDialogProvider"/> + <c>DialogService.ShowDialogAsync</c>
/// pipeline (no FluentUI-internal mocking). Covers the dialog's submit
/// pipeline: pick a GRADE → resolve coded value → GetOrCreate → subject
/// diff. The same pattern is shared with the upcoming
/// <c>GradeLevelEditDialogTests</c>; only the seeded assignments and the
/// grade id source differ.
/// </summary>
[TestClass]
public class GradeLevelCreateDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public GradeLevelCreateDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    /// <summary>Renders the dialog provider that hosts the dialogs shown in tests.</summary>
    private IRenderedComponent<FluentDialogProvider> RenderProvider() => Render<FluentDialogProvider>();

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
                    Content = new StringContent(hit.Body, Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {request.Method.Method} {url}", Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>
    /// Registers a scripted HTTP backend that answers the dialog's expected
    /// calls. <paramref name="seededAssignments"/> simulates any grade-subject
    /// rows that already exist for the (post-create) grade + period; the
    /// dialog's diff treats them as the baseline.
    /// </summary>
    private ScriptedHandler RegisterFor(
        Guid gradeId,
        Guid? currentPeriodId,
        IEnumerable<(Guid SubjectId, Guid AssignmentId)>? seededAssignments = null)
    {
        var handler = new ScriptedHandler();

        // CodedValueDropdown parent lookups + Subjects catalog — empty.
        handler.Map("/api/coded-values/", HttpStatusCode.OK, "[]");
        handler.Map("/students/subjects", HttpStatusCode.OK, "[]");

        // GetByIdAsync for the picked coded value (the dialog calls this on submit).
        handler.Map("GET", "/api/coded-values/", HttpStatusCode.OK,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid(),
                ["code"] = "GRADE5",
                ["name"] = "Grade 5",
                ["description"] = (string?)null,
                ["parentId"] = (Guid?)null,
                ["parentCode"] = (string?)null,
                ["isDisabled"] = false,
                ["displayOrder"] = 5,
                ["createdAt"] = DateTimeOffset.UnixEpoch,
                ["updatedAt"] = DateTimeOffset.UnixEpoch,
                ["attributes"] = Array.Empty<object>(),
                ["attributeDefinitions"] = Array.Empty<object>(),
                ["childrenCount"] = 0,
                ["isDeleted"] = false,
                ["deletedAt"] = (DateTimeOffset?)null,
                ["isOverridden"] = false,
                ["defaultName"] = (string?)null,
            }));

        // GetOrCreateGradeLevelAsync -> POST /students/grade-levels/get-or-create.
        handler.Map("POST", "/students/grade-levels/get-or-create", HttpStatusCode.OK,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = gradeId,
                ["codedValueId"] = Guid.NewGuid(),
                ["level"] = 5,
                ["name"] = "Grade 5",
                ["displayOrder"] = 5,
                ["subjectCount"] = 0,
                ["studentCount"] = 0,
                ["createdAt"] = DateTimeOffset.UnixEpoch,
                ["updatedAt"] = DateTimeOffset.UnixEpoch,
            }));

        // ListGradeSubjectsByGradeAsync -> GET /students/grade-subjects/by-grade/{id}/period/{pid}.
        var seeded = (seededAssignments ?? []).Select(a => new Dictionary<string, object?>
        {
            ["id"] = a.AssignmentId,
            ["gradeLevelId"] = gradeId,
            ["subjectId"] = a.SubjectId,
            ["periodId"] = currentPeriodId ?? Guid.Empty,
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        }).ToArray();
        handler.Map("GET", "/students/grade-subjects/by-grade/", HttpStatusCode.OK,
            JsonSerializer.Serialize(seeded));

        // AssignGradeSubjectAsync -> POST /students/grade-subjects.
        handler.Map("POST", "/students/grade-subjects", HttpStatusCode.Created,
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["id"] = Guid.NewGuid() }));
        // RemoveGradeSubjectAsync -> DELETE /students/grade-subjects/{id}.
        handler.Map("DELETE", "/students/grade-subjects/", HttpStatusCode.NoContent, "");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));

        return handler;
    }

    [TestMethod]
    public async Task Create_Dialog_Renders_Form_Fields()
    {
        // Register services BEFORE opening the dialog so its [Inject] students
        // api client resolves at instantiation time.
        RegisterFor(gradeId: Guid.NewGuid(), currentPeriodId: Guid.NewGuid());
        var cut = RenderProvider();

        var task = DialogService.ShowShellDialogAsync<GradeLevelCreateDialog, GradeLevelCreateDialog.GradeLevelCreateModel, SchoolCollab.Students.Admin.Services.GradeLevelDto>(
            new GradeLevelCreateDialog.GradeLevelCreateModel { CurrentPeriodId = Guid.NewGuid() },
            title: "Create grade level",
            size: DialogSize.Medium);

        // Wait for the EditForm to mount inside the provider.
        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        cut.Markup.Should().Contain("Grade");
        cut.Markup.Should().Contain("Age range");
        cut.Markup.Should().Contain("Allowed Gender");
        cut.Markup.Should().Contain("Create"); // submit button label

        // Cancel so the dialog task completes cleanly.
        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        var result = await task;
        result.Should().BeNull("cancelling closes the dialog with no result");
    }
}