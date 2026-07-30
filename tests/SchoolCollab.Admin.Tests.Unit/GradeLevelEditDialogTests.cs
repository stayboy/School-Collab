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
/// bUnit smoke tests for <see cref="GradeLevelEditDialog"/>. Mirrors the
/// Create-dialog suite (phase 3) so the Edit dialog's mount + cancel
/// path is exercised through the real
/// <see cref="FluentDialogProvider"/> + <c>DialogService.ShowDialogAsync</c>
/// pipeline. The full Edit submit + subject-diff suite lives in
/// follow-up phase 4.5 tests.
/// </summary>
[TestClass]
public class GradeLevelEditDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public GradeLevelEditDialogTests()
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
    /// Registers the scripted HTTP backend the Edit dialog talks to.
    /// The dialog calls ListSubjectsAsync (subject catalog),
    /// CodedValuesApi.GetByIdAsync (the grade coded value), and
    /// ListGradeSubjectsByGradeAsync (existing subject assignments).
    /// </summary>
    private ScriptedHandler RegisterFor(
        Guid gradeId,
        Guid codedValueId,
        Guid? currentPeriodId,
        IEnumerable<(Guid SubjectId, Guid AssignmentId)>? seededAssignments = null)
    {
        var handler = new ScriptedHandler();

        // Subject catalog - empty (no subjects in this fixture).
        handler.Map("/students/subjects", HttpStatusCode.OK, "[]");

        // CodedValueDropdown parent lookup (no-op when not used).
        handler.Map("/api/coded-values/", HttpStatusCode.OK, "[]");

        // CodedValuesApi.GetByIdAsync for the picked grade coded value -
        // the dialog uses this to capture Level / DisplayOrder.
        handler.Map("GET", "/api/coded-values/", HttpStatusCode.OK,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = codedValueId,
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

        // ListGradeSubjectsByGradeAsync - baseline assignments for the diff.
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

        // Assign / Remove - wired up so deeper phase-4.5 tests can reuse.
        handler.Map("POST", "/students/grade-subjects", HttpStatusCode.Created,
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["id"] = Guid.NewGuid() }));
        handler.Map("DELETE", "/students/grade-subjects/", HttpStatusCode.NoContent, "");
        // PUT grade-level validation fields.
        handler.Map("PUT", "/students/grade-levels/", HttpStatusCode.NoContent, "");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));

        return handler;
    }

    private IRenderedComponent<FluentDialogProvider> RenderProvider() => Render<FluentDialogProvider>();

    [TestMethod]
    public async Task Edit_Dialog_Renders_GradeName_Pencil_And_ValidationFields()
    {
        var gradeId = Guid.NewGuid();
        var codedValueId = Guid.NewGuid();
        RegisterFor(gradeId, codedValueId, currentPeriodId: Guid.NewGuid());
        var cut = RenderProvider();

        var model = new GradeLevelEditDialog.GradeLevelEditModel
        {
            Id = gradeId,
            CodedValueId = codedValueId,
            CurrentName = "Grade 5",
            MinAge = 10,
            MaxAge = 12,
            CurrentPeriodId = Guid.NewGuid(),
        };

        var task = DialogService.ShowShellDialogAsync<
            GradeLevelEditDialog,
            GradeLevelEditDialog.GradeLevelEditModel,
            SchoolCollab.Students.Admin.Services.GradeLevelDto>(
            model, "Edit grade - Grade 5", DialogSize.Medium);

        // Wait for the EditForm to mount inside the provider.
        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        cut.Markup.Should().Contain("Grade 5", "the dialog shows the resolved grade name");
        cut.Markup.Should().Contain("Age range");
        cut.Markup.Should().Contain("Allowed Gender");
        cut.Markup.Should().Contain("Save"); // submit button label
        cut.Markup.Should().Contain("Override the tenant display name",
            "the pencil icon button is rendered with a tooltip");

        // Cancel so the dialog task completes cleanly.
        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        var result = await task;
        result.Should().BeNull("cancelling closes the dialog with no result");
    }

    [TestMethod]
    public async Task Edit_Dialog_No_Current_Period_Hides_Subjects_Section()
    {
        var gradeId = Guid.NewGuid();
        var codedValueId = Guid.NewGuid();
        RegisterFor(gradeId, codedValueId, currentPeriodId: null);
        var cut = RenderProvider();

        var model = new GradeLevelEditDialog.GradeLevelEditModel
        {
            Id = gradeId,
            CodedValueId = codedValueId,
            CurrentName = "Grade 5",
            CurrentPeriodId = null, // no current period
        };

        var task = DialogService.ShowShellDialogAsync<
            GradeLevelEditDialog,
            GradeLevelEditDialog.GradeLevelEditModel,
            SchoolCollab.Students.Admin.Services.GradeLevelDto>(
            model, "Edit grade", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        // The Subjects row is gated on CurrentPeriodId - without one the
        // warning bar should show instead.
        cut.Markup.Should().Contain("No active period");

        var cancelButton = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();
        var result = await task;
        result.Should().BeNull();
    }

    }