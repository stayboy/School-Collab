using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
/// bUnit coverage for <see cref="QuestionGenerationSection"/> (Step-2
/// AI generation controls). Per the round-3 plan's binding coverage list:
/// <list type="bullet">
///   <item>GateEnabled=true + fake generator returning 3 dtos → rows
///     appended, OnQuestionsChanged invoked, error cleared.</item>
///   <item>GateEnabled=false → Generate disabled + DisabledTooltip; no
///     generator call.</item>
///   <item>Fake throws <see cref="QuestionGenerationFailed"/> → the
///     message renders, Generate remains enabled for retry, second click
///     calls again.</item>
///   <item>TopicId null → click shows "Select a subject…" and the fake
///     records zero calls; count field has min="1".</item>
///   <item>Cancel: fake awaits Task.Delay(…, ct); Generate then Cancel →
///     OperationCanceledException swallowed, Model.Questions unchanged,
///     no error bar (EC-2).</item>
/// </list>
///
/// Follows the in-round convention from <c>AssignmentCreateBunitTests</c>
/// (MSTest + FluentAssertions + bUnit). Uses
/// <see cref="FakeQuestionGenerator"/> (an inline fake) per the plan;
/// Moq only for <see cref="ILogger{T}"/>.
/// </summary>
[TestClass]
public class QuestionGenerationSectionBunitTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;

    public QuestionGenerationSectionBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton(Mock.Of<ILogger<QuestionGenerationSection>>());

        // R3 (P1-2): the section now records a generation header before the rows land, so it needs
        // the Assignments client. Registered always; the route itself is added per render so a test
        // that sets AssignmentId gets a deterministic generation id and one that does not never calls.
        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");
        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
    }

    private IRenderedComponent<QuestionGenerationSection> RenderSection(
        AssignmentEditFormModel model,
        FakeQuestionGenerator fake,
        bool gateEnabled = true,
        bool promptLocked = false,
        Guid? topicId = null,
        string? topicName = null,
        EventCallback? onQuestionsChanged = null,
        IUrlTextExtractor? urlExtractor = null,
        Guid? assignmentId = null)
    {
        Services.AddSingleton<IAssignmentQuestionGenerator>(fake);
        // WS-B2 (step 6): the section depends on the reference-URL extraction
        // seam. A benign fake (always succeeds) by default; a caller may
        // supply its own to exercise the 4-URL cap / per-URL failure paths.
        Services.AddSingleton<IUrlTextExtractor>(urlExtractor ?? DefaultUrlExtractor());

        // R3: the generation-header route. Registered last, so a test can pre-register its own
        // capture handler for the same URL and win (MockHttp matches in registration order).
        if (assignmentId is not null)
        {
            _mockHttp.When(HttpMethod.Post, "http://localhost/assignments/*/question-generations")
                .Respond(HttpStatusCode.OK, "application/json",
                    $"{{\"generationId\":\"{GenerationId}\"}}");
        }

        return Render<QuestionGenerationSection>(parameters =>
        {
            parameters.Add(p => p.Model, model);
            parameters.Add(p => p.GateEnabled, gateEnabled);
            parameters.Add(p => p.PromptLocked, promptLocked);
            parameters.Add(p => p.TopicId, topicId);
            parameters.Add(p => p.TopicName, topicName);
            parameters.Add(p => p.AssignmentId, assignmentId);
            parameters.Add(p => p.OnQuestionsChanged,
                onQuestionsChanged ?? EventCallback.Empty);
        });
    }

    /// <summary>R3: the id the fake Assignments API returns for a recorded generation header.</summary>
    private static readonly Guid GenerationId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [TestMethod]
    public void GateEnabled_GenerateClick_AppendsRowsAndInvokesOnQuestionsChanged()
    {
        var model = new AssignmentEditFormModel();
        // Pre-seed a hand-written row to verify append-on-generate (decision (d)).
        var seed = QuestionEditorRow.NewMultipleChoice();
        seed.QuestionText = "Hand-written";
        model.Questions.Add(seed);

        var fake = new FakeQuestionGenerator();
        var changedFired = 0;
        var cb = EventCallback.Factory.Create(this, () => changedFired++);
        var topicId = Guid.NewGuid();

        var cut = RenderSection(
            model,
            fake,
            gateEnabled: true,
            topicId: topicId,
            topicName: "Photosynthesis",
            onQuestionsChanged: cb);

        // Find and click the Generate button. The button text is "Generate"
        // (only "Generating…" while a call is in flight).
        var generateButton = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal));
        generateButton.Click();

        cut.WaitForAssertion(() =>
        {
            fake.GenerateCalls.Should().Be(1, "the Generate button calls the generator exactly once");
        });
        cut.WaitForAssertion(() =>
        {
            model.Questions.Should().HaveCount(4, "1 pre-seeded + 3 generated rows");
        });
        cut.WaitForAssertion(() =>
        {
            model.Questions[0].QuestionText.Should().Be("Hand-written",
                "the pre-seeded row survives ahead of the appended ones (decision (d))");
            model.Questions[1].QuestionText.Should().Be("G-Q1");
            model.Questions[2].QuestionText.Should().Be("G-Q2");
            model.Questions[3].QuestionText.Should().Be("G-Q3");
        });
        cut.WaitForAssertion(() =>
        {
            changedFired.Should().Be(1, "OnQuestionsChanged fires after a successful append");
        });
        cut.WaitForAssertion(() =>
        {
            for (var i = 0; i < model.Questions.Count; i++)
            {
                model.Questions[i].DisplayOrder.Should().Be(i,
                    "DisplayOrder is re-indexed 0..n (EC-7)");
            }
        });
    }

    [TestMethod]
    public void GateDisabled_GenerateButton_IsDisabledAndShowsTooltip_NoGeneratorCall()
    {
        var model = new AssignmentEditFormModel();
        var fake = new FakeQuestionGenerator();

        var cut = RenderSection(
            model,
            fake,
            gateEnabled: false,
            topicId: Guid.NewGuid(),
            topicName: "Topic");

        var generateButton = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal));
        generateButton.HasAttribute("disabled").Should().BeTrue(
            "EC-3: the Generate button must be disabled when the gate is closed");
        cut.Markup.Should().Contain(QuestionGenerationGate.DisabledTooltip,
            "the DisabledTooltip text is rendered for the user");

        // Click is a no-op when disabled — the button is a fluent web component,
        // and bUnit click on a disabled button does not dispatch the event. Even
        // if it did, the OnGenerateAsync guard `if (!GateEnabled || _generating) return;`
        // makes the fake unreachable.
        try
        {
            generateButton.Click();
        }
        catch
        {
            // Disabled buttons throw on click in some browsers / bUnit versions;
            // that's still a guarantee no call was made.
        }

        fake.GenerateCalls.Should().Be(0, "no generator call when gate is closed");
        model.Questions.Should().BeEmpty();
    }

    [TestMethod]
    public void GeneratorThrowsQuestionGenerationFailed_MessageRenders_GenerateRemainsEnabled_RetryCallsAgain()
    {
        var model = new AssignmentEditFormModel();
        var fake = new FakeQuestionGenerator
        {
            NextException = new QuestionGenerationFailed("Provider rate-limited; please try again."),
        };
        var topicId = Guid.NewGuid();

        var cut = RenderSection(
            model,
            fake,
            gateEnabled: true,
            topicId: topicId,
            topicName: "Topic");

        var generateButton = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal));
        generateButton.Click();

        cut.WaitForAssertion(() =>
        {
            fake.GenerateCalls.Should().Be(1);
        });
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Provider rate-limited; please try again.",
                "EC-1/EC-8: the typed exception's message is rendered for the user");
            model.Questions.Should().BeEmpty("a failed generation does not append rows");
        });

        // After the failure the Generate button must remain enabled so the user
        // can retry — the EC-1/EC-8 contract.
        cut.WaitForAssertion(() =>
        {
            var retryButton = cut.FindAll("fluent-button")
                .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal));
            retryButton.HasAttribute("disabled").Should().BeFalse(
                "the Generate button must remain enabled after a failure so the user can retry");
        });

        // Switch the fake back to a successful response and click again.
        fake.NextException = null;
        var retryButton2 = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal));
        retryButton2.Click();

        cut.WaitForAssertion(() =>
        {
            fake.GenerateCalls.Should().Be(2, "a retry clicks the button again");
            model.Questions.Should().HaveCount(3, "a successful retry appends rows");
        });
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().NotContain("Provider rate-limited; please try again.",
                "the error message clears on a subsequent successful append");
        });
    }

    [TestMethod]
    public void ConfigSummary_ReflectsTheKnobs()
    {
        var model = new AssignmentEditFormModel
        {
            DifficultyEasyCount = 4,
            DifficultyMediumCount = 4,
            DifficultyHardCount = 2,
        };

        var cut = RenderSection(model, new FakeQuestionGenerator(), topicName: "Photosynthesis");

        var summary = cut.Find("#cq-config-summary").TextContent;
        summary.Should().Contain("5 questions", "QA-3: the summary leads with the count knob");
        summary.Should().Contain("4 easy / 4 medium / 2 hard", "QA-3: the difficulty mix is shown");
        summary.Should().Contain("Multiple choice", "PB-4: the summary reuses the canonical type names");
    }

    [TestMethod]
    public void Generate_RefreshesATemplateMatchedNarrative_NamingTheCurrentSubject()
    {
        // A previously composed narrative (PB-4 shape) naming a subject the page no longer has.
        var model = new AssignmentEditFormModel
        {
            AiPromptOverride =
                "Generate 5 questions for the subject \"Old Subject\".\n" +
                "Grade level: —.\n" +
                "Difficulty mix: 0 easy / 0 medium / 0 hard.\n" +
                "Include: Multiple choice, True / false, Short answer.",
        };
        var cut = RenderSection(
            model, new FakeQuestionGenerator(), topicId: Guid.NewGuid(), topicName: "Photosynthesis");

        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            model.AiPromptOverride.Should().Contain("\"Photosynthesis\"",
                "QA-23: a template-matched narrative is refreshed for the current context at Generate time");
            model.AiPromptOverride.Should().NotContain("Old Subject");
        });
    }

    [TestMethod]
    public void Generate_LeavesAHandWrittenPromptAlone()
    {
        var model = new AssignmentEditFormModel { AiPromptOverride = "Keep it gentle and short." };
        var cut = RenderSection(
            model, new FakeQuestionGenerator(), topicId: Guid.NewGuid(), topicName: "Photosynthesis");

        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => model.Questions.Should().NotBeEmpty());

        model.AiPromptOverride.Should().Be("Keep it gentle and short.",
            "QA-23/PB-5: hand-written prose is never overwritten by the refresh");
    }

    [TestMethod]
    public void TopicIdNull_ClickShowsSelectASubject_NoGeneratorCall()
    {
        var model = new AssignmentEditFormModel();
        var fake = new FakeQuestionGenerator();

        var cut = RenderSection(
            model,
            fake,
            gateEnabled: true,
            topicId: null, // <-- the no-call guarantee
            topicName: null);

        var generateButton = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal));
        generateButton.Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Select a subject before generating questions.",
                "a null TopicId surfaces a friendly 'select a subject' message");
        });

        fake.GenerateCalls.Should().Be(0,
            "TopicId null must short-circuit BEFORE the generator is called (no-call guarantee)");
        model.Questions.Should().BeEmpty();

        // R3 (QA-22/D14): the count field moved into the prompt dialog, so its EC-10 min="1"
        // guard now lives in QuestionPromptDialogBunitTests; the composer proves its NEW surface
        // instead — the read-only summary line and the Configure trigger.
        cut.Markup.Should().Contain("id=\"cq-config-summary\"",
            "QA-3: the composer shows the read-only config summary");
        cut.Markup.Should().Contain("id=\"cq-configure\"",
            "QA-4: the composer shows the Configure trigger");
    }

    [TestMethod]
    public void GeneratorThrowsUnhandledException_FriendlyErrorRenders_GenerateRemainsEnabled()
    {
        // Tester iteration 1 fix (P2): a non-typed exception (e.g.
        // HttpRequestException, JsonException, ObjectDisposedException)
        // must not escape to the page-level ErrorBoundary — it would
        // permanently replace the wizard. The catch-all in the section
        // logs + surfaces the same friendly error state with retry.
        var model = new AssignmentEditFormModel();
        var fake = new FakeQuestionGenerator
        {
            NextException = new InvalidOperationException("upstream blew up"),
        };
        var topicId = Guid.NewGuid();

        var cut = RenderSection(
            model,
            fake,
            gateEnabled: true,
            topicId: topicId,
            topicName: "Topic");

        var generateButton = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal));
        generateButton.Click();

        cut.WaitForAssertion(() =>
        {
            fake.GenerateCalls.Should().Be(1, "the first click reaches the generator");
        });
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain(
                "Something went wrong while generating questions. Please try again.",
                "the catch-all surfaces a friendly retryable message — no exception escapes to ErrorBoundary");
            model.Questions.Should().BeEmpty("a failed generation does not append rows");
        });

        // After the failure the Generate button must remain enabled so
        // the user can retry (the EC-1/EC-8 contract).
        cut.WaitForAssertion(() =>
        {
            var retryButton = cut.FindAll("fluent-button")
                .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal));
            retryButton.HasAttribute("disabled").Should().BeFalse(
                "the Generate button must remain enabled after a non-typed failure so the user can retry");
        });
    }

    [TestMethod]
    public void Cancel_OperationCanceledException_Swallowed_ListUnchanged_NoErrorBar_EC2()
    {
        var model = new AssignmentEditFormModel();
        var seed = QuestionEditorRow.NewMultipleChoice();
        seed.QuestionText = "Pre-existing";
        model.Questions.Add(seed);

        var fake = new FakeQuestionGenerator
        {
            Delay = TimeSpan.FromMilliseconds(500), // give Cancel time to fire
        };

        var topicId = Guid.NewGuid();
        var cut = RenderSection(
            model,
            fake,
            gateEnabled: true,
            topicId: topicId,
            topicName: "Topic");

        var generateButton = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal));
        generateButton.Click();

        // While the call is in flight, the Cancel button is rendered.
        cut.WaitForAssertion(() =>
        {
            var cancelButton = cut.FindAll("fluent-button")
                .FirstOrDefault(b => b.TextContent.Trim().StartsWith("Cancel", StringComparison.Ordinal));
            cancelButton.Should().NotBeNull(
                "the Cancel button renders while the generator is awaiting");
            cancelButton!.Click();
        });

        cut.WaitForAssertion(() =>
        {
            fake.GenerateCalls.Should().Be(1, "the generate call did fire before the cancel");
            model.Questions.Should().HaveCount(1,
                "EC-2: cancel leaves the question list untouched");
            model.Questions[0].QuestionText.Should().Be("Pre-existing");
        });
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ToLowerInvariant().Should().NotContain("error",
                "EC-2: cancel does NOT surface an error message");
        });
    }

    // ── R3-2 (F1): the attachment-side budget drop is surfaced, never silent ──

    [TestMethod]
    public async Task BudgetFull_DroppedAttachment_SurfacesAnAttachmentWarning()
    {
        // F1: 3 reference URLs + 3 readable attachments = 6 candidates for 5 slots, so the third
        // attachment never grounds this generation — while its row still reads as successfully extracted.
        // The URL half already warns about its own losses; the attachment half must do the same.
        var model = new AssignmentEditFormModel { Title = "T" };
        model.AddResourceUrl("https://example.com/a");
        model.AddResourceUrl("https://example.com/b");
        model.AddResourceUrl("https://example.com/c");
        for (var i = 0; i < 3; i++)
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
        var cut = RenderSection(model, fake, topicId: Guid.NewGuid(), topicName: "Topic");
        var generateButton = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal));
        await cut.InvokeAsync(() => generateButton.Click());

        cut.WaitForAssertion(() => fake.LastRequest.Should().NotBeNull());
        fake.LastRequest!.ResourceTexts.Should().HaveCount(ResourceTextBudget.MaxResourceTexts,
            "three URL texts plus two attachments fill the five-slot budget");
        fake.LastRequest.ResourceTexts.Should().NotContain("attachment body 2",
            "the third attachment is the one the full budget drops");

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain(
                "1 attachment(s) were read but not included in this generation",
                "the author is told an attachment they can see as extracted did not ground the generation");
            cut.Markup.Should().Contain("grounding budget is full",
                "the warning names the cause, so the remedy (free a slot) is actionable");
        });
    }

    [TestMethod]
    public async Task NothingDroppedFromTheBudget_RendersNoAttachmentWarning()
    {
        // The negative half: a warning that fires on every generation would train the author to ignore
        // it. One URL + two attachments = 3 candidates for 5 slots — nothing is lost, so nothing is said.
        var model = new AssignmentEditFormModel { Title = "T" };
        model.AddResourceUrl("https://example.com/a");
        for (var i = 0; i < 2; i++)
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
        var cut = RenderSection(model, fake, topicId: Guid.NewGuid(), topicName: "Topic");
        var generateButton = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal));
        await cut.InvokeAsync(() => generateButton.Click());

        cut.WaitForAssertion(() => fake.LastRequest.Should().NotBeNull());
        fake.LastRequest!.ResourceTexts.Should().HaveCount(3,
            "the setup really did put candidates in front of the budget, so the no-warning assertion " +
            "below is not vacuous");
        cut.Markup.Should().NotContain("were read but not included in this generation",
            "nothing was dropped, so no attachment warning is rendered");
    }

    // ── WS-B2 (round-10 binding list): difficulty, lock, URL cases ────────

    [TestMethod]
    public void Difficulty_Fields_ThreadIntoRequest()
    {
        var model = new AssignmentEditFormModel
        {
            DifficultyEasyCount = 2,
            DifficultyMediumCount = 4,
            DifficultyHardCount = 1,
        };
        var fake = new FakeQuestionGenerator();
        var topicId = Guid.NewGuid();

        var cut = RenderSection(model, fake, topicId: topicId, topicName: "Topic");
        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => fake.LastRequest.Should().NotBeNull());
        fake.LastRequest!.TopicId.Should().Be(topicId);
        fake.LastRequest.DifficultyEasyCount.Should().Be(2,
            "R7: the request reads the difficulty counts from the form model");
        fake.LastRequest.DifficultyMediumCount.Should().Be(4);
        fake.LastRequest.DifficultyHardCount.Should().Be(1);
    }

    [TestMethod]
    public void PromptLocked_DisablesTextArea()
    {
        var model = new AssignmentEditFormModel();
        var fake = new FakeQuestionGenerator();

        var cut = RenderSection(model, fake, promptLocked: true, topicId: Guid.NewGuid(), topicName: "Topic");

        var area = cut.FindComponent<FluentTextArea>();
        area.Instance.Disabled.Should().BeTrue(
            "when the tenant locks the org prompt, the guidance textarea is disabled");
        cut.Markup.Should().Contain("Your organization has locked the AI prompt.",
            "the locked tooltip explains why the guidance is disabled");
    }

    [TestMethod]
    public void ResourceUrls_PassExtractedTexts()
    {
        var model = new AssignmentEditFormModel { Title = "T" };
        model.AddResourceUrl("https://example.com/a");
        model.AddResourceUrl("https://example.com/b");
        var fake = new FakeQuestionGenerator();
        var extractor = Mock.Of<IUrlTextExtractor>(x =>
            x.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new UrlTextExtractionResult(true, "content", null)));

        var cut = RenderSection(
            model, fake, topicId: Guid.NewGuid(), topicName: "Topic",
            urlExtractor: extractor);
        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => fake.LastRequest.Should().NotBeNull());
        fake.LastRequest!.ResourceTexts.Should().NotBeNull(
            "reference-URL texts are passed to the generator (decision g)");
        fake.LastRequest.ResourceTexts!.Should().HaveCount(2, "one extracted text per included URL");
    }

    // ── R3 (criteria 4/6; P1-1/P1-2/P1-6): attachment grounding + the generation header ──

    [TestMethod]
    public void Attachments_GroundTheGeneration_InsideTheSharedBudget_AndAnUnreadableOneIsDropped()
    {
        // Criterion 6 (presence-asserted): with two attachments, one readable, the captured request
        // carries the READABLE one's extracted text inside ResourceTexts, within the budget shared with
        // the URL texts. Before R3 nothing but URL text could reach ResourceTexts at all.
        var model = new AssignmentEditFormModel { Title = "T" };
        model.AddResourceUrl("https://example.com/a");
        model.AddAttachment(new AttachmentEditorRow
        {
            FileName = "readable.pdf", ContentType = "application/pdf", FileSize = 10, StoragePath = "p1",
            ExtractionStatus = AttachmentExtractionStatusDto.Succeeded, ExtractedText = "attachment body",
        });
        model.AddAttachment(new AttachmentEditorRow
        {
            FileName = "unreadable.pdf", ContentType = "application/pdf", FileSize = 10, StoragePath = "p2",
            ExtractionStatus = AttachmentExtractionStatusDto.Failed, ExtractionError = "unreadable",
        });
        var fake = new FakeQuestionGenerator();
        var extractor = Mock.Of<IUrlTextExtractor>(x =>
            x.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new UrlTextExtractionResult(true, "url body", null)));

        var cut = RenderSection(
            model, fake, topicId: Guid.NewGuid(), topicName: "Topic", urlExtractor: extractor);
        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => fake.LastRequest.Should().NotBeNull());
        fake.LastRequest!.ResourceTexts.Should().Equal(
            ["url body", "attachment body"],
            "the URL text comes first and the readable attachment fills the next slot; the unreadable " +
            "attachment contributes nothing");
    }

    [TestMethod]
    public void NoAttachments_LeavesTheRequestByteEquivalentToThePreR3UrlOnlyRequest()
    {
        // Criterion 6's second half. The pre-R3 composition was exactly the list of successful URL
        // texts (or null when there were none) — reproduced literally here.
        var model = new AssignmentEditFormModel { Title = "T" };
        model.AddResourceUrl("https://example.com/a");
        var fake = new FakeQuestionGenerator();
        var extractor = Mock.Of<IUrlTextExtractor>(x =>
            x.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new UrlTextExtractionResult(true, "url body", null)));
        IReadOnlyList<string>? preR3 = ["url body"];

        var cut = RenderSection(
            model, fake, topicId: Guid.NewGuid(), topicName: "Topic", urlExtractor: extractor);
        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => fake.LastRequest.Should().NotBeNull());
        fake.LastRequest!.ResourceTexts.Should().Equal(preR3!);
    }

    [TestMethod]
    public void UnsupportedAttachment_ContributesNoText_AndDoesNotBlockGeneration()
    {
        var model = new AssignmentEditFormModel { Title = "T" };
        model.AddAttachment(new AttachmentEditorRow
        {
            FileName = "photo.png", ContentType = "image/png", FileSize = 10, StoragePath = "p1",
            ExtractionStatus = AttachmentExtractionStatusDto.Unsupported,
            ExtractionError = "'.png' files are not extracted (PDF and DOCX only).",
        });
        var fake = new FakeQuestionGenerator();

        var cut = RenderSection(model, fake, topicId: Guid.NewGuid(), topicName: "Topic");
        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => fake.GenerateCalls.Should().Be(1));
        fake.LastRequest!.ResourceTexts.Should().BeNull(
            "an unsupported upload yields no grounding text but never blocks the generation");
    }

    [TestMethod]
    public void Generate_WithAnAssignment_RecordsOneHeader_RelayingTheAiHostsResolvedModel()
    {
        // P1-1: the recorded provider/model must be the AI host's own resolution, relayed verbatim.
        var model = new AssignmentEditFormModel { Title = "T" };
        var fake = new FakeQuestionGenerator { NextProvider = "openrouter", NextModel = "google/gemma-4-31b-it" };
        string? capturedHeaderBody = null;
        _mockHttp.When(HttpMethod.Post, "http://localhost/assignments/*/question-generations")
            .Respond(request =>
            {
                capturedHeaderBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"generationId\":\"{GenerationId}\"}}",
                        System.Text.Encoding.UTF8, "application/json"),
                };
            });

        var cut = RenderSection(
            model, fake, topicId: Guid.NewGuid(), topicName: "Topic",
            assignmentId: Guid.Parse("55555555-5555-5555-5555-555555555555"));
        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => capturedHeaderBody.Should().NotBeNull(
            "an Edit-mode generation records exactly one header row"));
        capturedHeaderBody.Should().Contain("\"provider\":\"openrouter\"");
        capturedHeaderBody.Should().Contain("\"model\":\"google/gemma-4-31b-it\"");
        capturedHeaderBody.Should().Contain("\"questionCount\":5");
    }

    [TestMethod]
    public void GeneratedRows_CarryTheRecordedGenerationId()
    {
        // P1-2: the header id is stamped onto every produced row BEFORE the first save can re-mint
        // them, so provenance is on the payload rather than lost.
        var model = new AssignmentEditFormModel { Title = "T" };
        var fake = new FakeQuestionGenerator();

        var cut = RenderSection(
            model, fake, topicId: Guid.NewGuid(), topicName: "Topic",
            assignmentId: Guid.Parse("55555555-5555-5555-5555-555555555555"));
        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => model.Questions.Should().HaveCount(3));
        model.Questions.Should().OnlyContain(q => q.GenerationId == GenerationId);
        model.Questions.Should().HaveCount(3, "every row of this generation points at the one header");
    }

    [TestMethod]
    public void CreateMode_RecordsNoHeader_AndLeavesProvenanceNull()
    {
        // No assignment row exists yet on a Create, so there is nothing to hang a header off. The rows
        // must still be produced (the request itself succeeded) and simply carry no provenance.
        var model = new AssignmentEditFormModel { Title = "T" };
        var fake = new FakeQuestionGenerator();

        var cut = RenderSection(model, fake, topicId: Guid.NewGuid(), topicName: "Topic");
        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => model.Questions.Should().HaveCount(3));
        model.Questions.Should().OnlyContain(q => q.GenerationId == null);
    }

    [TestMethod]
    public void ResourceUrl_Failure_FailsOpenWithWarning()
    {
        var model = new AssignmentEditFormModel { Title = "T" };
        model.AddResourceUrl("https://example.com/broken");
        var fake = new FakeQuestionGenerator();
        // Per-URL failure is fail-open: no extracted text, but generation proceeds.
        var extractor = Mock.Of<IUrlTextExtractor>(x =>
            x.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new UrlTextExtractionResult(false, null, "blocked")));

        var cut = RenderSection(
            model, fake, topicId: Guid.NewGuid(), topicName: "Topic",
            urlExtractor: extractor);
        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => fake.LastRequest.Should().NotBeNull());
        fake.LastRequest!.ResourceTexts.Should().BeNull(
            "a failed URL extraction contributes no text but does not block generation");
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("https://example.com/broken",
                "the per-URL failure surfaces a warning naming the failed link");
            cut.Markup.Should().Contain("Couldn't read content",
                "the warning text says generation proceeds without the failed link");
        });
        model.Questions.Should().HaveCount(3, "the generator still appends rows after a per-URL failure");
    }

    [TestMethod]
    public void MoreThanThreeUrls_WarnsAndCaps()
    {
        var model = new AssignmentEditFormModel { Title = "T" };
        for (var i = 0; i < 4; i++)
        {
            model.AddResourceUrl($"https://example.com/{i}");
        }
        var fake = new FakeQuestionGenerator();

        var cut = RenderSection(model, fake, topicId: Guid.NewGuid(), topicName: "Topic");
        cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Generate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => fake.LastRequest.Should().NotBeNull());
        fake.LastRequest!.ResourceTexts.Should().HaveCount(3,
            "only the first 3 included URLs are fetched (decision g cap)");
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Only the first 3 included links are fetched",
                "the over-cap warning is surfaced to the teacher");
            cut.Markup.Should().Contain("1 link(s) skipped",
                "the warning reports how many links were skipped");
        });
    }

    // ── Fake IAssignmentQuestionGenerator ──────────────────────────────

    /// <summary>Default URL extractor: always succeeds with a fixed text.</summary>
    private static IUrlTextExtractor DefaultUrlExtractor() => Mock.Of<IUrlTextExtractor>(x =>
        x.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
            == Task.FromResult(new UrlTextExtractionResult(true, "extracted", null)));


    /// <summary>Hand-rolled fake (per the plan: fakes for non-HTTP
    /// interfaces; Moq only for ILogger-style seams). Stands in for
    /// <see cref="IAssignmentQuestionGenerator"/> in bUnit tests so the
    /// question-generation flow can be exercised deterministically.</summary>
    private sealed class FakeQuestionGenerator : IAssignmentQuestionGenerator
    {
        public FakeQuestionGenerator()
        {
            NextResults = new List<GeneratedQuestionDto>
            {
                new("G-Q1", GeneratedQuestionType.MultipleChoice,
                    new[] { new GeneratedQuestionOptionDto("A", true), new GeneratedQuestionOptionDto("B") }),
                new("G-Q2", GeneratedQuestionType.TrueFalse,
                    new[] { new GeneratedQuestionOptionDto("True", true), new GeneratedQuestionOptionDto("False") }),
                new("G-Q3", GeneratedQuestionType.ShortAnswer, null, "model"),
            };
        }

        public int GenerateCalls { get; private set; }

        /// <summary>R7: the last <see cref="QuestionGenerationRequest"/> the
        /// generator saw, for the difficulty/URL threading assertions.</summary>
        public QuestionGenerationRequest? LastRequest { get; private set; }

        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        public Exception? NextException { get; set; }

        public IReadOnlyList<GeneratedQuestionDto> NextResults { get; set; }

        /// <summary>R3 (P1-1): the provider/model the fake "AI host" reports it resolved. The UI
        /// relays these onto the generation-header request; the tests assert the relayed values are
        /// the fake's own, i.e. that nothing client-side invents or overrides a model.</summary>
        public string? NextProvider { get; set; } = "ollama";

        /// <summary>See <see cref="NextProvider"/>.</summary>
        public string? NextModel { get; set; } = "gemma4:31b-cloud";

        public async Task<QuestionGenerationResponse> GenerateAsync(
            QuestionGenerationRequest request,
            CancellationToken ct = default)
        {
            GenerateCalls++;
            LastRequest = request;
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, ct);
            }
            if (NextException is not null)
            {
                throw NextException;
            }
            return new QuestionGenerationResponse(NextResults, NextProvider, NextModel);
        }
    }
}
