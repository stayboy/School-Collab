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

    /// <summary>R3 (P1-2): the header id the fake Assignments API returns; the staged draft must
    /// carry it on every question row.</summary>
    private static readonly Guid GenerationId = Guid.Parse("44444444-4444-4444-4444-444444444444");

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
        EventCallback? onConfirmed = null,
        AssignmentEditFormModel? model = null,
        bool expand = true,
        // R4 (CP-8): the same page-supplied pick names compartment 6 receives.
        IReadOnlyList<string>? contextStrandNames = null,
        IReadOnlyList<string>? contextLessonNames = null)
    {
        Services.AddSingleton<IAssignmentQuestionGenerator>(fake);

        // R3 (P1-2): the draft-stage write point records a generation header first; the default route
        // returns a fixed id so the staged draft's GenerationId is deterministic.
        _mockHttp.When(HttpMethod.Post, "http://localhost/assignments/*/question-generations")
            .Respond(HttpStatusCode.OK, "application/json",
                $"{{\"generationId\":\"{GenerationId}\"}}");

        var cut = Render<QuestionsDraftSection>(parameters =>
        {
            parameters.Add(p => p.AssignmentId, AssignmentId);
            parameters.Add(p => p.Model, model ?? new AssignmentEditFormModel());
            parameters.Add(p => p.GateEnabled, gateEnabled);
            parameters.Add(p => p.PromptLocked, promptLocked);
            parameters.Add(p => p.TopicId, TopicId);
            parameters.Add(p => p.TopicName, "Photosynthesis");
            parameters.Add(p => p.ContextStrandNames, contextStrandNames ?? []);
            parameters.Add(p => p.ContextLessonNames, contextLessonNames ?? []);
            parameters.Add(p => p.OnConfirmed, onConfirmed ?? EventCallback.Empty);
        });

        // QA-18/D12 (round content-questions-modern-ui): the "Draft preview" panel is collapsed by
        // default, so the shared fixture expands it and drives the controls exactly as before. The
        // collapsed default itself is asserted by DraftPanel_IsCollapsedByDefault_ThenExpands.
        if (expand)
        {
            // Explicit 5s budget: the initial GET + render can exceed bUnit's 1s default under a
            // loaded full-solution run (this wait timed out once on CI-shaped load).
            cut.WaitForAssertion(() => cut.Find("#cq-draft-panel"), TimeSpan.FromSeconds(5));
            if (string.Equals(cut.Find("#cq-draft-panel").GetAttribute("aria-expanded"), "false", StringComparison.OrdinalIgnoreCase))
            {
                cut.Find("#cq-draft-panel").Click();
            }
            cut.WaitForAssertion(() => cut.Find("#cq-draft-body").Should().NotBeNull(), TimeSpan.FromSeconds(5));
        }
        return cut;
    }

    /// <summary>QA-18/D12: the disclosure starts collapsed and toggles open.</summary>
    [TestMethod]
    public void DraftPanel_IsCollapsedByDefault_ThenExpands()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);

        var cut = RenderSection(new FakeQuestionGenerator(), expand: false);

        cut.WaitForAssertion(() =>
        {
            var toggle = cut.Find("#cq-draft-panel");
            toggle.GetAttribute("aria-expanded").Should().Be("false",
                "the draft panel starts collapsed so its duplicate controls do not weigh on the page");
            cut.FindAll("#cq-draft-body").Should().BeEmpty();
        });

        cut.Find("#cq-draft-panel").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("#cq-draft-panel").GetAttribute("aria-expanded").Should().Be("true");
            cut.Find("#cq-draft-body").Should().NotBeNull();
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

    /// <summary>R4 (CP-8): the draft surface threads the SAME pick names compartment 6 gets — its
    /// generation rides the one shared composed-prompt seam, so its context is the page's, not a
    /// second copy that could drift.</summary>
    [TestMethod]
    public void DraftGeneration_CarriesThePickedStrandsAndSummary()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);
        _mockHttp.When(HttpMethod.Put, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);

        var fake = new FakeQuestionGenerator();
        var cut = RenderSection(
            fake,
            model: new AssignmentEditFormModel(),
            contextStrandNames: ["Fractions"],
            contextLessonNames: ["Equivalent fractions"]);

        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => fake.GenerateCalls.Should().Be(1));
        fake.LastRequest!.ContextStrands.Should().Equal(new[] { "Fractions" },
            "CP-8/D24: the draft surface's generation carries the picked strand names too");
        cut.Find("#cq-draft-config-summary").TextContent.Should()
            .Contain(" · Strands: Fractions")
            .And.Contain(" · Lessons: Equivalent fractions", "CP-7: the same inline summary shape");
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

    // ── R3 (P1-2): the draft-stage write point ──────────────────────────

    [TestMethod]
    public void Generate_StagesADraftWhoseQuestionsAllCarryTheRecordedGenerationId()
    {
        // P1-2: the header is written at draft-stage / generate time and its id rides INSIDE the draft
        // blob, so ConfirmQuestionsDraftCommandHandler's re-mint can re-attach it. If this hop dropped it,
        // the author's first save would silently strip provenance from every generated question.
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);
        string? stagedBody = null;
        _mockHttp.When(HttpMethod.Put, "http://localhost/assignments/*/questions-draft")
            .Respond(request =>
            {
                stagedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

        var fake = new FakeQuestionGenerator();
        var cut = RenderSection(fake);

        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => stagedBody.Should().NotBeNull("the draft is staged after the header is recorded"));
        stagedBody.Should().Contain($"\"generationId\":\"{GenerationId}\"");
        // One occurrence per drafted question — three here — plus nothing else that looks like it.
        stagedBody!.Split("generationId").Length.Should().Be(
            4, "every drafted question carries the header id (3 rows = 4 split fragments)");
    }

    [TestMethod]
    public void Generate_WithStagedAttachments_GroundsTheDraftOnTheirExtractedText()
    {
        // P1-6/AI-3: the kebab's "Generate questions" action anchors to this surface, so attachment
        // grounding must behave here exactly as it does in compartment 6.
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);
        _mockHttp.When(HttpMethod.Put, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);

        var model = new AssignmentEditFormModel();
        model.AddAttachment(new AttachmentEditorRow
        {
            FileName = "readable.pdf", ContentType = "application/pdf", FileSize = 10, StoragePath = "p1",
            ExtractionStatus = AttachmentExtractionStatusDto.Succeeded, ExtractedText = "attachment body",
        });
        var fake = new FakeQuestionGenerator();
        var cut = RenderSection(fake, model: model);

        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => fake.LastRequest.Should().NotBeNull());
        fake.LastRequest!.ResourceTexts.Should().Equal(
            ["attachment body"],
            "the extracted attachment text grounds the draft through the same ResourceTexts seam");
    }

    [TestMethod]
    public void Generate_WithMoreAttachmentsThanTheBudget_SurfacesTheDroppedAttachmentWarning()
    {
        // R3-2: the draft surface's attachment half gets the same surface as compartment 6. Six readable
        // attachments, five slots — the sixth cannot ground this draft, and the author must be told here
        // too (this is the surface the kebab's "Generate questions" action anchors to).
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);
        _mockHttp.When(HttpMethod.Put, "http://localhost/assignments/*/questions-draft")
            .Respond(HttpStatusCode.NoContent);

        var model = new AssignmentEditFormModel();
        for (var i = 0; i < 6; i++)
        {
            model.AddAttachment(new AttachmentEditorRow
            {
                FileName = $"readable-{i}.pdf", ContentType = "application/pdf", FileSize = 10,
                StoragePath = $"p{i}",
                ExtractionStatus = AttachmentExtractionStatusDto.Succeeded,
                ExtractedText = $"attachment body {i}",
            });
        }

        var fake = new FakeQuestionGenerator();
        var cut = RenderSection(fake, model: model);

        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => fake.LastRequest.Should().NotBeNull());
        fake.LastRequest!.ResourceTexts.Should().HaveCount(ResourceTextBudget.MaxResourceTexts,
            "the draft path fills the budget from slot zero with attachment text");
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain(
                "1 attachment(s) were read but not included in this generation",
                "the draft surface warns about the dropped attachment exactly as compartment 6 does");
            cut.Markup.Should().Contain("grounding budget is full");
        });
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
        }, TimeSpan.FromSeconds(5));
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

        /// <summary>R3 (P1-6): the last request the section built, so the shared ResourceTexts budget
        /// can be asserted on the draft surface too.</summary>
        public QuestionGenerationRequest? LastRequest { get; private set; }

        public Task<QuestionGenerationResponse> GenerateAsync(
            QuestionGenerationRequest request,
            CancellationToken ct = default)
        {
            GenerateCalls++;
            LastRequest = request;
            return Task.FromResult(new QuestionGenerationResponse(
                new[]
                {
                    new GeneratedQuestionDto("G-Q1", GeneratedQuestionType.MultipleChoice,
                        new[] { new GeneratedQuestionOptionDto("A", true), new GeneratedQuestionOptionDto("B") }),
                    new GeneratedQuestionDto("G-Q2", GeneratedQuestionType.TrueFalse,
                        new[] { new GeneratedQuestionOptionDto("True", true), new GeneratedQuestionOptionDto("False") }),
                    new GeneratedQuestionDto("G-Q3", GeneratedQuestionType.ShortAnswer, null, "model"),
                },
                // R3 (P1-1): the AI host's own resolved tuple, which the draft section relays onto
                // the generation-header request.
                Provider: "ollama",
                Model: "gemma4:31b-cloud"));
        }
    }
}
