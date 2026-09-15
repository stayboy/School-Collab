using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.AI.Abstractions;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// bUnit coverage for <see cref="QuestionsDraftSection"/> (WS-B2 / spec §3.4
/// line 73 — the Draft-only regenerate/confirm/discard surface). Per the
/// round-10 plan's binding coverage list:
/// <list type="bullet">
///   <item>Generate → generator called → staged via the Api → the preview
///     renders the drafted questions.</item>
///   <item>Confirm → the Api confirm route is hit and <c>OnConfirmed</c> is
///     raised; the preview clears.</item>
///   <item>Discard → the Api discard route is hit and the preview clears.</item>
///   <item>PromptLocked → the guidance textarea is disabled.</item>
///   <item>An already-staged draft (GET on init) pre-loads the preview.</item>
/// </list>
///
/// Uses a hand-rolled <see cref="FakeQuestionGenerator"/>, Moq for the
/// <see cref="ILogger{T}"/>, and RichardSzalay.MockHttp over a real
/// <see cref="HttpClient"/> for the sealed <see cref="AssignmentsApiClient"/>
/// (the in-round convention). Distinct question text per row keeps every
/// <c>@key</c> unique (the ar-9 fixture-defect class).
/// </summary>
[TestClass]
public class QuestionsDraftSectionBunitTests : BunitContext
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TopicId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;

    public QuestionsDraftSectionBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        _apiJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter<QuestionTypeDto>(),
                new JsonStringEnumConverter<AssignmentTypeDto>(),
                new JsonStringEnumConverter<GradingFormatDto>(),
                new JsonStringEnumConverter<TargetAudienceTypeDto>(),
                new JsonStringEnumConverter<AssignmentStatusDto>(),
            }
        };

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(NullLogger<QuestionsDraftSection>.Instance);
    }

    private IRenderedComponent<QuestionsDraftSection> RenderSection(
        FakeQuestionGenerator fake,
        bool gateEnabled = true,
        bool promptLocked = false,
        EventCallback? onConfirmed = null)
    {
        Services.AddSingleton<IAssignmentQuestionGenerator>(fake);

        return Render<QuestionsDraftSection>(parameters =>
        {
            parameters.Add(p => p.AssignmentId, AssignmentId);
            parameters.Add(p => p.GateEnabled, gateEnabled);
            parameters.Add(p => p.PromptLocked, promptLocked);
            parameters.Add(p => p.TopicId, TopicId);
            parameters.Add(p => p.TopicName, "Photosynthesis");
            parameters.Add(p => p.GradeLevelId, (Guid?)null);
            parameters.Add(p => p.OnConfirmed, onConfirmed ?? EventCallback.Empty);
        });
    }

    [TestMethod]
    public void Generate_Then_Stages_Draft()
    {
        // GET on init returns 204 → no pre-existing draft.
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);
        _mockHttp.When(HttpMethod.Put, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);

        var fake = new FakeQuestionGenerator();
        var cut = RenderSection(fake);

        // Click Generate (button text starts with "Generate").
        var generate = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal));
        generate.Click();

        cut.WaitForAssertion(() => fake.GenerateCalls.Should().Be(1));
        cut.WaitForAssertion(() =>
        {
            // The drafted questions render in the read-only preview.
            cut.Markup.Should().Contain("G-Q1");
            cut.Markup.Should().Contain("G-Q2");
            cut.Markup.Should().Contain("G-Q3");
        });
        _mockHttp.VerifyNoOutstandingExpectation();
    }

    [TestMethod]
    public void Confirm_CallsClient_And_RaisesChanged()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);
        _mockHttp.When(HttpMethod.Put, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);
        // Confirm route returns the refreshed summary.
        var summary = SampleSummary();
        _mockHttp.When(HttpMethod.Post, "http://localhost/assignments/*/questions-draft/confirm")
            .Respond(HttpStatusCode.OK, "application/json",
                JsonSerializer.Serialize(summary, _apiJsonOptions));

        var fake = new FakeQuestionGenerator();
        var confirmedFired = 0;
        var cb = EventCallback.Factory.Create(this, () => confirmedFired++);
        var cut = RenderSection(fake, onConfirmed: cb);

        // Stage a draft first.
        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("G-Q1"));

        // Confirm &amp; replace.
        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Confirm", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => confirmedFired.Should().Be(1,
            "confirm raises OnConfirmed so the Edit page reloads the summary"));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().NotContain("G-Q1",
                "the preview clears after a successful confirm");
        });
        _mockHttp.VerifyNoOutstandingExpectation();
    }

    [TestMethod]
    public void Discard_ClearsPreview()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);
        _mockHttp.When(HttpMethod.Put, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);
        _mockHttp.When(HttpMethod.Delete, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);

        var fake = new FakeQuestionGenerator();
        var cut = RenderSection(fake);

        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("G-Q1"));

        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Discard", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().NotContain("G-Q1", "the preview clears after discard"));
        _mockHttp.VerifyNoOutstandingExpectation();
    }

    [TestMethod]
    public void PromptLocked_DisablesGuidance()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);

        var fake = new FakeQuestionGenerator();
        var cut = RenderSection(fake, promptLocked: true);

        var textarea = cut.FindComponent<FluentTextArea>();
        textarea.Instance.Disabled.Should().BeTrue(
            "when the tenant locks the org prompt, the guidance textarea is disabled (WS-B2)");
        cut.Markup.Should().Contain("Your organization has locked the AI prompt.",
            "the locked placeholder explains why the guidance is disabled");
        _mockHttp.VerifyNoOutstandingExpectation();
    }

    [TestMethod]
    public void Renders_Preload_ExistingDraft()
    {
        // GET on init returns an already-staged draft (survives page reloads).
        var draft = new[]
        {
            new NewQuestionDto("Existing Q1", QuestionTypeDto.MultipleChoice, 0, new[]
            {
                new NewQuestionOptionDto("A", true),
                new NewQuestionOptionDto("B", false),
            }),
            new NewQuestionDto("Existing Q2", QuestionTypeDto.ShortAnswer, 1, null, "model"),
        };
        var body = JsonSerializer.Serialize(new { questions = draft }, _apiJsonOptions);
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.OK, "application/json", body);

        var fake = new FakeQuestionGenerator();
        var cut = RenderSection(fake);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Existing Q1");
            cut.Markup.Should().Contain("Existing Q2");
        });
        _mockHttp.VerifyNoOutstandingExpectation();
    }

    private static AssignmentSummaryDto SampleSummary() => new(
        Id: Guid.NewGuid(),
        Title: "Sample",
        Description: null,
        AssignmentType: AssignmentTypeDto.Digital,
        GradingFormat: GradingFormatDto.AutoGraded,
        TargetAudienceType: TargetAudienceTypeDto.AllStudents,
        TopicId: TopicId,
        TopicName: "Photosynthesis",
        GradeLevelId: null,
        GradeName: null,
        Status: AssignmentStatusDto.Draft,
        DueDate: null,
        MaxScore: null,
        MandatoryReview: false,
        CreatedByTeacherId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    /// <summary>Hand-rolled fake (per the plan) standing in for
    /// <see cref="IAssignmentQuestionGenerator"/> so the generate→stage flow
    /// is exercised deterministically. Returns three distinct questions.</summary>
    private sealed class FakeQuestionGenerator : IAssignmentQuestionGenerator
    {
        public int GenerateCalls { get; private set; }

        public Task<IReadOnlyList<GeneratedQuestionDto>> GenerateAsync(
            QuestionGenerationRequest request,
            CancellationToken ct = default)
        {
            GenerateCalls++;
            return Task.FromResult<IReadOnlyList<GeneratedQuestionDto>>(new[]
            {
                new GeneratedQuestionDto("G-Q1", GeneratedQuestionType.MultipleChoice,
                    new[] { new GeneratedQuestionOptionDto("A", true), new GeneratedQuestionOptionDto("B") }),
                new GeneratedQuestionDto("G-Q2", GeneratedQuestionType.TrueFalse,
                    new[] { new GeneratedQuestionOptionDto("True", true), new GeneratedQuestionOptionDto("False") }),
                new GeneratedQuestionDto("G-Q3", GeneratedQuestionType.ShortAnswer, null, "model"),
            });
        }
    }
}
