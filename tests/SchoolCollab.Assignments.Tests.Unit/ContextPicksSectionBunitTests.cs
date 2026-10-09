using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Services;
using Authoring = SchoolCollab.Assignments.Application.Components.Pages.Assignments.AssignmentAuthoring;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// R4 (spec §6A CP-1/CP-2/CP-3/CP-11; round <c>assignment-context-strands-lessons</c>) — the Basics
/// "Strands &amp; lessons" row (AC-9) and its CP-11 save-payload filter as the SURFACE applies them
/// (AC-12).
///
/// <para>The first block renders the feature-local <see cref="ContextPicksSection"/> directly (the
/// states CP-3 names, the chips, the notes). The second renders the real
/// <see cref="AssignmentAuthoring"/> page — the row's place in Basics, the CP-4 subject-change
/// hook, the G31 per-picked-strand lessons fetch and the payload the Edit save actually issues.</para>
///
/// <para><b>Discriminating:</b> the row, the two pickers and the subject-change hook do not exist on
/// the pre-R4 tree — absence is the discriminator for the page-level cases, and the section-level
/// ones fail against a missing component outright.</para>
/// </summary>
[TestClass]
public class ContextPicksSectionBunitTests : BunitContext
{
    private static readonly Guid StrandA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StrandB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LessonA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid LessonB = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DanglingStrand = Guid.Parse("55555555-5555-5555-5555-555555555555");

    // ── The section in isolation: CP-3's states and the chips ────────────────

    private IRenderedComponent<ContextPicksSection> RenderSection(
        AssignmentEditFormModel model,
        ContextPicksSection.ContextPickOption[]? strandOptions = null,
        ContextPicksSection.ContextPickOption[]? lessonOptions = null,
        bool strandsLoaded = true,
        bool lessonsLoaded = true,
        bool disabled = false,
        string? disabledReason = null,
        Action? onStrandPicksChanged = null)
    {
        var strands = strandOptions ?? [new ContextPicksSection.ContextPickOption(StrandA, "Fractions")];
        var lessons = lessonOptions ?? [new ContextPicksSection.ContextPickOption(LessonA, "Equivalent fractions")];

        return Render<ContextPicksSection>(parameters =>
        {
            parameters.Add(p => p.Model, model);
            parameters.Add(p => p.StrandOptions, strands);
            parameters.Add(p => p.LessonOptions, lessons);
            parameters.Add(p => p.StrandNames, strands.ToDictionary(o => o.Id, o => o.Label));
            parameters.Add(p => p.LessonNames, lessons.ToDictionary(o => o.Id, o => o.Label));
            parameters.Add(p => p.StrandsLoaded, strandsLoaded);
            parameters.Add(p => p.LessonsLoaded, lessonsLoaded);
            parameters.Add(p => p.Disabled, disabled);
            parameters.Add(p => p.DisabledReason, disabledReason ?? (disabled ? ContextPicksSection.NoSubjectReason : null));
            parameters.Add(p => p.StrandPicksChanged,
                onStrandPicksChanged is null ? EventCallback.Empty : EventCallback.Factory.Create(this, onStrandPicksChanged));
        });
    }

    /// <summary>D4: the two add actions the section renders (replacing the retired multi-selects),
    /// plus the chip strips beneath them.</summary>
    [TestMethod]
    public void RendersTwoAddButtons_WithStableIds()
    {
        var cut = RenderSection(new AssignmentEditFormModel());

        cut.Find("#authoring-add-strand").Should().NotBeNull("D4: the strand add action");
        cut.Find("#authoring-add-lesson").Should().NotBeNull("D4: the lesson add action");
        cut.Find("#authoring-basics-strand-chips").Should().NotBeNull(
            "picks render as chips beneath the buttons");
        cut.Find("#authoring-basics-lesson-chips").Should().NotBeNull();
        cut.FindAll("fluent-select[multiple], fluent-select[multiple='true']").Should().BeEmpty(
            "D4: the two multi-selects are retired in favour of the buttons + dialog");
    }

    [TestMethod]
    public void NoSubject_DisablesBothAddButtonsWithTheReason()
    {
        var cut = RenderSection(new AssignmentEditFormModel(), disabled: true);

        cut.Find("#authoring-context-picks-reason").TextContent.Trim()
            .Should().Be(ContextPicksSection.NoSubjectReason, "CP-3: disabled WITH a reason, never hidden");
        cut.Find("#authoring-add-strand").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#authoring-add-lesson").HasAttribute("disabled").Should().BeTrue();
    }

    [TestMethod]
    public void SubjectWithNoStrands_RendersTheEmptySourceNote_NotTheNothingPickedNote()
    {
        var cut = RenderSection(new AssignmentEditFormModel(), strandOptions: []);

        cut.Find("#authoring-strands-none").TextContent.Trim().Should().Be(ContextPicksSection.NoStrandsText);
        cut.FindAll("#authoring-strands-none").Should().ContainSingle();
    }

    [TestMethod]
    public void StrandsWithNothingPicked_SaysTheAiUsesTheWholeSubject()
    {
        var cut = RenderSection(new AssignmentEditFormModel());

        cut.Find("#authoring-strands-none").TextContent.Trim().Should().Be(ContextPicksSection.NoStrandPickedText);
    }

    [TestMethod]
    public void NoLessons_RendersTheLessonsRowOwnNoneNote()
    {
        var cut = RenderSection(new AssignmentEditFormModel(), lessonOptions: []);

        cut.Find("#authoring-lessons-none").TextContent.Trim().Should().Be(ContextPicksSection.NoLessonsText);
    }

    [TestMethod]
    public void UnloadedList_RendersNoEmptyNote()
    {
        // P8-4: a list that never loaded is NOT an authoritative empty list, so neither CP-3's
        // "no strands yet" nor "(removed)" may be derived from it.
        var cut = RenderSection(new AssignmentEditFormModel(), strandsLoaded: false, lessonsLoaded: false);

        cut.FindAll("#authoring-strands-none").Should().BeEmpty();
        cut.FindAll("#authoring-lessons-none").Should().BeEmpty();
    }

    /// <summary>Registers a mocked <see cref="IDialogService"/> that answers the strand/lesson
    /// picker open with a fixed OK payload (the <c>RegisterPublishDialog</c>/<c>ContactsEditorTests</c>
    /// shell-dialog pattern), so the write-back path after the dialog runs for real.</summary>
    private void RegisterContextPickDialog(params Guid[] selectedIds)
    {
        var dialogRef = new Mock<IDialogReference>();
        var payload = new DialogShellResult<ContextPickDialogResult>(
            new ContextPickDialogResult(selectedIds));
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Ok<object?>(payload)));

        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<ContextPickDialog, DialogShellData<ContextPickDialogModel>>(
                It.IsAny<DialogShellData<ContextPickDialogModel>>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);

        Services.AddSingleton(dialogMock.Object);
    }

    [TestMethod]
    public void PickingAStrand_ThroughTheDialog_AddsAChip_AndNotifiesThePage()
    {
        var model = new AssignmentEditFormModel();
        var notified = 0;
        RegisterContextPickDialog(StrandA);
        var cut = RenderSection(model, onStrandPicksChanged: () => notified++);

        cut.Find("#authoring-add-strand").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-basics-strand-chips .chip-label")
                .Select(e => e.TextContent.Trim()).Should().Contain("Fractions");
            model.ContextStrandIds.Should().Equal(new[] { StrandA },
                "the dialog's OK writes the picks back to the model");
            notified.Should().Be(1,
                "the page must re-read the lesson options for the newly picked strands (G31)");
        });
    }

    [TestMethod]
    public void LessonsNeverLoaded_DisablesOnlyTheLessonAddButton()
    {
        // P8-4: a list that never loaded is not an authoritative list — the button that would
        // offer it stays disabled, while the strand half (loaded here) remains usable.
        var cut = RenderSection(new AssignmentEditFormModel(), lessonsLoaded: false);

        cut.Find("#authoring-add-lesson").HasAttribute("disabled").Should().BeTrue(
            "an unreadable lesson list is never offered (spec §9.7)");
        cut.Find("#authoring-add-strand").HasAttribute("disabled").Should().BeFalse(
            "the strand list itself is loaded and pickable");
        cut.FindAll("#authoring-lessons-none").Should().BeEmpty(
            "the empty-source note is equally not derivable from a fetch that never happened");
    }

    [TestMethod]
    public void DismissingAChip_RemovesThePick()
    {
        var model = new AssignmentEditFormModel();
        model.LoadContextPicks([StrandA], [LessonA]);
        var cut = RenderSection(model);

        var chip = cut.FindComponents<Chip>().Single(c => c.Instance.Label == "Fractions");
        cut.InvokeAsync(() => chip.Instance.OnDismiss.InvokeAsync());

        model.ContextStrandIds.Should().BeEmpty("CP-2: a chip dismisses the pick it names");
        model.ContextLessonIds.Should().Equal(new[] { LessonA }, "and only that pick");
    }

    [TestMethod]
    public void DanglingPick_RendersTheRemovedMarker()
    {
        var model = new AssignmentEditFormModel();
        // The strand list LOADED and does not resolve this id — CP-11's premise.
        model.LoadContextPicks([DanglingStrand], []);
        var cut = RenderSection(model);

        cut.Find("#authoring-basics-strand-chips").TextContent.Should()
            .Contain(ContextPicksSection.RemovedPickMarker,
                "CP-11: a pick the loaded list does not resolve is marked (removed)");
    }

    [TestMethod]
    public void UnloadedList_DoesNotMarkThePickAsRemoved()
    {
        var model = new AssignmentEditFormModel();
        model.LoadContextPicks([DanglingStrand], []);
        // The strand list never loaded, so nothing about this id is known — NOT the (removed) state.
        var cut = RenderSection(model, strandsLoaded: false);

        cut.Find("#authoring-basics-strand-chips").TextContent.Should()
            .NotContain(ContextPicksSection.RemovedPickMarker,
                "a failed fetch is not evidence the strand is gone (P8-4)");
    }

    // ── The page: the row's place in Basics, the CP-4 hook, the save payload ──

    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;
    private static readonly Guid GradeId = Guid.Parse("00000000-0000-0000-0000-0000000000cc");
    private readonly List<AssignmentTargetDto> _targets =
    [
        new AssignmentTargetDto(TargetKindDto.GradeLevel, GradeId, 0)
    ];
    private AssignmentAuthoringChildrenDto _children = new(Guid.NewGuid(), [], [], [], []);
    private TopicStrandDto[] _strands = [];
    private TopicLessonDto[] _lessons = [];
    private readonly List<string> _lessonQueries = [];
    private string? _putBody;

    public ContextPicksSectionBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        _apiJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter<AssignmentTypeDto>(),
                new JsonStringEnumConverter<AssignmentStatusDto>(),
                new JsonStringEnumConverter<GradingFormatDto>(),
                new JsonStringEnumConverter<TargetAudienceTypeDto>(),
                new JsonStringEnumConverter<QuestionTypeDto>(),
                new JsonStringEnumConverter<ResourceKindDto>(),
                new JsonStringEnumConverter<AttachmentExtractionStatusDto>(),
                new JsonStringEnumConverter<ModuleTypeDto>(),
                new JsonStringEnumConverter<TargetKindDto>(),
            }
        };

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");
        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton<StudentsApiClient>();
        Services.AddSingleton<SchoolCollab.Admin.Shared.Services.CodedValuesApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<StudentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<Authoring>>());
        Services.AddSingleton(Mock.Of<IAssignmentQuestionGenerator>());
        Services.AddSingleton(Mock.Of<IUrlTextExtractor>());
        Services.AddSingleton<IFeatureFlagService>(new StubFlagService());

        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/ai-prompt-policy")
            .Respond(HttpStatusCode.OK, "application/json", "{\"aiPromptLocked\":false}");
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/effective-policy*")
            .Respond(HttpStatusCode.OK, "application/json",
                JsonSerializer.Serialize(new EffectiveAssignmentPolicyResolver().Resolve(
                    tenantDefault: new AssignmentPolicyFields(), gradeOverride: null)));
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/grade-levels")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/grade-levels/*/streams")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        _mockHttp.When(HttpMethod.Get, "http://localhost/activity-groups*")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/recipient-preview*")
            .Respond(HttpStatusCode.OK, "application/json",
                "{\"studentsMatched\":0,\"primaryContacts\":0,\"otherContacts\":0,\"previewDegraded\":false}");

        // R4: the two pick sources. Registered once; the test's own arrays drive their content, and
        // the lessons matcher records every strand-scoped query so G31's per-strand fetch is assertable.
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/topics/*/strands")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(_strands ?? [], _apiJsonOptions), Encoding.UTF8, "application/json")
            });
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/topics/*/lessons*")
            .Respond(request =>
            {
                _lessonQueries.Add(request.RequestUri!.PathAndQuery);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(_lessons ?? [], _apiJsonOptions), Encoding.UTF8, "application/json")
                };
            });
    }

    private sealed class StubFlagService : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => true;
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(true);
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private static Guid TopicId { get; } = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static Guid OtherTopicId { get; } = Guid.Parse("00000000-0000-0000-0000-0000000000bb");

    private AssignmentSummaryDto MakeDto(Guid? topicId = null) =>
        new(
            Id: Guid.NewGuid(),
            Title: "Math HW",
            Description: null,
            AssignmentType: AssignmentTypeDto.Manual,
            GradingFormat: GradingFormatDto.TeacherGraded,
            TargetAudienceType: TargetAudienceTypeDto.SelectedGrades,
            TopicId: topicId ?? TopicId,
            TopicName: "Math",
            Status: AssignmentStatusDto.Draft,
            DueDate: null,
            MaxScore: null,
            MandatoryReview: true,
            CreatedByTeacherId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            TargetGradeIds: [GradeId]);

    private IRenderedComponent<Authoring> RenderEdit(
        AssignmentSummaryDto dto,
        IReadOnlyList<Guid>? strandIds = null,
        IReadOnlyList<Guid>? lessonIds = null,
        bool captureSave = false)
    {
        // UX-7 (D2): the page imports its collocated beforeunload module once the form is dirty,
        // and OnAfterRenderAsync awaits that import. bUnit must own the module or the await has no
        // responder — the passing AssignmentAuthoringBunitTests harness sets it up the same way.
        JSInterop.SetupModule(Authoring.BeforeUnloadModulePath);
        _children = new AssignmentAuthoringChildrenDto(
            dto.Id, [], [], [], _targets, strandIds ?? [], lessonIds ?? []);

        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/authoring")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(_children, _apiJsonOptions));
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/groups")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/questions-draft")
            .Respond(HttpStatusCode.NoContent);
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(dto, _apiJsonOptions));
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/subjects/by-grade/*")
            .Respond(HttpStatusCode.OK, "application/json", "[]");

        if (captureSave)
        {
            _mockHttp.When(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}")
                .Respond(request =>
                {
                    _putBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                });
        }

        return EnterEditFields(Render<Authoring>(parameters =>
        {
            parameters.Add(p => p.Mode, AssignmentAuthoringMode.Edit);
            parameters.Add(p => p.Id, dto.Id);
        }));
    }

    /// <summary>assignment-create-edit-redesign D1: the Edit route opens on the SUMMARY; these
    /// page-level cases assert the form, so the pencil flip runs here (same seam as the
    /// <c>AssignmentAuthoringBunitTests</c> helper). No-op when no pencil renders.</summary>
    private static IRenderedComponent<Authoring> EnterEditFields(IRenderedComponent<Authoring> cut)
    {
        cut.WaitForAssertion(() =>
        {
            (cut.FindAll("#authoring-summary").Count > 0 || cut.FindAll("#authoring-basics").Count > 0)
                .Should().BeTrue("the surface must have settled before the pencil decision");
        }, TimeSpan.FromSeconds(5));

        var pencil = cut.FindAll("#authoring-summary-edit");
        if (pencil.Count > 0)
        {
            pencil.Single().Click();
            cut.WaitForAssertion(() =>
                cut.FindAll("#authoring-basics").Count.Should().BeGreaterThan(0,
                    "the pencil flips the page into the edit-fields view"), TimeSpan.FromSeconds(5));
        }

        return cut;
    }

    private static IElement OwningFormRow(IRenderedComponent<Authoring> cut, string id)
    {
        var element = cut.Find($"#{id}");
        while (element is not null && !element.ClassList.Contains("form-row"))
        {
            element = element.ParentElement;
        }

        return element ?? throw new InvalidOperationException($"#{id} renders outside any FormRow");
    }

    /// <summary>Clicks the Edit action bar's "Save draft" kebab item (the update path) — the
    /// <c>AssignmentAuthoringBunitTests</c> helper, whose primary action on a Draft is Publish.</summary>
    private static void SaveDraft(IRenderedComponent<Authoring> cut)
    {
        cut.Find("fluent-button[title=\"More assignment actions\"]").Click();
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item")
                .Should().Contain(i => i.TextContent.Trim() == "Save draft"),
            TimeSpan.FromSeconds(5));
        cut.FindAll("fluent-menu-item").Single(i => i.TextContent.Trim() == "Save draft").Click();
    }

    private static FluentSelect<Authoring.PickerOption> PagePicker(IRenderedComponent<Authoring> cut, string id) =>
        cut.FindComponents<FluentSelect<Authoring.PickerOption>>().Single(s => s.Instance.Id == id).Instance;

    [TestMethod]
    public void CreateMode_RendersTheRowBeneathSubject_WithBothAddButtonsDisabled()
    {
        var cut = Render<Authoring>(parameters => parameters.Add(p => p.Mode, AssignmentAuthoringMode.Create));

        cut.WaitForAssertion(() =>
        {
            // Re-query BOTH rows inside the wait: the page's async load re-renders the Basics
            // section, so an element captured before it settles is no longer in a fresh
            // `.form-row` list and `IndexOf` returns -1 (observed as a one-off flake under load).
            var rows = cut.FindAll(".form-row").ToList();
            var strandRow = OwningFormRow(cut, "authoring-add-strand");
            strandRow.TextContent.Should().Contain("Strands & lessons", "X-1/CP-1: the row is labelled in Basics");
            rows.IndexOf(strandRow).Should().BeGreaterThan(
                rows.IndexOf(OwningFormRow(cut, "authoring-basics-subject")),
                "…and it sits BENEATH the Subject row (the CP-1 position)");
        });

        cut.Find("#authoring-context-picks-reason").TextContent.Trim()
            .Should().Be(ContextPicksSection.NoSubjectReason, "CP-3: no subject yet, so both buttons say why");
        cut.Find("#authoring-add-strand").HasAttribute("disabled").Should().BeTrue();
        cut.Find("#authoring-add-lesson").HasAttribute("disabled").Should().BeTrue();
        cut.FindAll("section#authoring-basics h3.authoring-compartment-title").Should().ContainSingle(
            "X-1: the section renders no title of its own — the Basics h3 stays the only one");
    }

    [TestMethod]
    public void EditMode_RendersTheRowBeneathSubject()
    {
        var cut = RenderEdit(MakeDto());
        cut.WaitForAssertion(() => cut.FindAll("#authoring-add-strand").Should().NotBeEmpty(),
            TimeSpan.FromSeconds(5));

        cut.WaitForAssertion(() =>
        {
            // Same stale-reference guard as the Create-mode test: re-query both rows each pass.
            var rows = cut.FindAll(".form-row").ToList();
            var strandRow = OwningFormRow(cut, "authoring-add-strand");
            strandRow.TextContent.Should().Contain("Strands & lessons");
            rows.IndexOf(strandRow).Should().BeGreaterThan(
                rows.IndexOf(OwningFormRow(cut, "authoring-basics-subject")));
        });
    }

    [TestMethod]
    public void EditWithPicks_LoadsThemAsChips_AndOffersThem()
    {
        _strands = [Strand(StrandA, "Fractions"), Strand(StrandB, "Decimals")];
        _lessons = [Lesson(LessonA, "Equivalent fractions", StrandA)];

        var cut = RenderEdit(MakeDto(), strandIds: [StrandA], lessonIds: [LessonA]);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#authoring-basics-strand-chips").TextContent.Should().Contain("Fractions",
                "the persisted picks round-trip onto the Edit surface");
            cut.Find("#authoring-basics-lesson-chips").TextContent.Should().Contain("Equivalent fractions");
        });

        // G31: the lessons offered follow the PICKED strands (one fetch per picked strand).
        _lessonQueries.Should().Contain(q => q.Contains($"strandId={StrandA}", StringComparison.OrdinalIgnoreCase),
            "the lessons source is read per picked strand");
    }

    [TestMethod]
    public void EditWithDanglingPick_MarksItRemoved_AndFiltersItFromTheSavePayload()
    {
        // The strand list loaded and resolves StrandA only: the other pick is CP-11's dangling case.
        _strands = [Strand(StrandA, "Fractions")];
        _lessons = [Lesson(LessonA, "Equivalent fractions", StrandA)];

        var dto = MakeDto();
        var cut = RenderEdit(dto, strandIds: [DanglingStrand, StrandA], lessonIds: [], captureSave: true);

        cut.WaitForAssertion(() => cut.Find("#authoring-basics-strand-chips").TextContent
            .Should().Contain(ContextPicksSection.RemovedPickMarker));

        SaveDraft(cut);

        cut.WaitForAssertion(() => _putBody.Should().NotBeNull("the save really issued the update"));
        _putBody.Should().Contain($"\"contextStrandIds\":[\"{StrandA}\"]",
            "CP-11: the dangling id is filtered out of the save payload while the resolved pick rides");
        _putBody.Should().NotContain(DanglingStrand.ToString(),
            "…and nowhere else in the payload either");
    }

    [TestMethod]
    public void SubjectChange_ClearsThePicks_AndReReadsTheSources()
    {
        _strands = [Strand(StrandA, "Fractions")];
        _lessons = [Lesson(LessonA, "Equivalent fractions", StrandA)];

        var cut = RenderEdit(MakeDto(), strandIds: [StrandA], lessonIds: [LessonA]);
        cut.WaitForAssertion(() => cut.Find("#authoring-basics-strand-chips").TextContent.Should().Contain("Fractions"));

        var queriesBefore = _lessonQueries.Count;

        // Drive the subject picker's real callback with a DIFFERENT subject (CP-4).
        // The by-grade stub returns no options, so there is no OtherTopicId item in Items; invoking
        // the callback directly with a constructed option still exercises the subject-change hook.
        var subjectPicker = PagePicker(cut, "authoring-basics-subject");
        var other = new Authoring.PickerOption(OtherTopicId.ToString(), "Other subject");
        cut.InvokeAsync(() => subjectPicker.SelectedOptionChanged.InvokeAsync(other));

        cut.WaitForAssertion(() =>
        {
            cut.Find("#authoring-basics-strand-chips").TextContent.Trim().Should().BeEmpty(
                "CP-4: a strand belongs to a subject, so changing the subject clears the picks");
            cut.Find("#authoring-basics-lesson-chips").TextContent.Trim().Should().BeEmpty();
        });
        _lessonQueries.Count.Should().BeGreaterThan(queriesBefore,
            "…and the strand/lesson sources are re-read for the new topic");
    }

    private static TopicStrandDto Strand(Guid id, string name) =>
        new(id, TopicId, null, name, null, null, null, false, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static TopicLessonDto Lesson(Guid id, string name, Guid strandId) =>
        new(id, TopicId, strandId, name, null, null, null, false, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
