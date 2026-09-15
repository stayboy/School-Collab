using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
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
    public QuestionGenerationSectionBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton(Mock.Of<ILogger<QuestionGenerationSection>>());
    }

    private IRenderedComponent<QuestionGenerationSection> RenderSection(
        AssignmentEditFormModel model,
        FakeQuestionGenerator fake,
        bool gateEnabled = true,
        bool promptLocked = false,
        Guid? topicId = null,
        string? topicName = null,
        Guid? gradeLevelId = null,
        EventCallback? onQuestionsChanged = null,
        IUrlTextExtractor? urlExtractor = null)
    {
        Services.AddSingleton<IAssignmentQuestionGenerator>(fake);
        // WS-B2 (step 6): the section depends on the reference-URL extraction
        // seam. A benign fake (always succeeds) by default; a caller may
        // supply its own to exercise the 4-URL cap / per-URL failure paths.
        Services.AddSingleton<IUrlTextExtractor>(urlExtractor ?? DefaultUrlExtractor());

        return Render<QuestionGenerationSection>(parameters =>
        {
            parameters.Add(p => p.Model, model);
            parameters.Add(p => p.GateEnabled, gateEnabled);
            parameters.Add(p => p.PromptLocked, promptLocked);
            parameters.Add(p => p.TopicId, topicId);
            parameters.Add(p => p.TopicName, topicName);
            parameters.Add(p => p.GradeLevelId, gradeLevelId);
            parameters.Add(p => p.OnQuestionsChanged,
                onQuestionsChanged ?? EventCallback.Empty);
        });
    }

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
            gradeLevelId: null,
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

        // The count field renders with min=\"1\" (EC-10 unreachable-zero).
        cut.Markup.Should().Contain("min=\"1\"",
            "EC-10: the count field's HTML attribute is min=1 so zero is unreachable via the input");
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

        public async Task<IReadOnlyList<GeneratedQuestionDto>> GenerateAsync(
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
            return NextResults;
        }
    }
}
