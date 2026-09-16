using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Families.Services;

namespace SchoolCollab.Families.Tests.Unit;

/// <summary>
/// WS-D2 (slice 2b) — the ward assignment player. Binding coverage:
/// <list type="bullet">
///   <item><c>Modules_Render_InOrder</c> — module sequence renders in DisplayOrder.</item>
///   <item><c>QuestionsLocked_Shows_GateHint</c> — when <c>QuestionsUnlocked == false</c> the player
///       shows the gate hint plus a per-module mark-read control.</item>
///   <item><c>QuestionsUnlocked_Shows_Submit</c> — when unlocked the submit area is present.</item>
///   <item><c>Submit_Triggers_ClientCall</c> — submitting invokes the client's submit endpoint.</item>
/// </list>
/// The video heartbeat JS itself is not executed under bUnit (JSInterop loose mode);
/// binding-level wiring is exercised instead.
/// </summary>
[TestClass]
public class WardAssignmentPlayerBunitTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;
    private static readonly Guid AssignmentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid StudentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Module1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Module2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter<ModuleTypeDto>(),
            new JsonStringEnumConverter<WardSubmissionStateDto>()
        }
    };

    // The host serves its own wwwroot at the app root (MapStaticAssets Root mode);
    // App.razor sets <base href="/"> so this root-relative path resolves on the deep
    // player route.
    private const string WardPlayerModulePath = "./js/wardPlayer.js";

    // The client deserializes with string-encoded enums (FamiliesJson.Options), so
    // submissions must be serialized with the same convention for a faithful round-trip.
    private static readonly JsonSerializerOptions JsonSubmission = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public WardAssignmentPlayerBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        _mockHttp = new MockHttpMessageHandler();
        var http = _mockHttp.ToHttpClient();
        http.BaseAddress = new Uri("http://localhost");
        Services.AddSingleton(http);
        Services.AddSingleton(new DeepLinkProtector(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()));
        Services.AddSingleton<FamiliesApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<FamiliesApiClient>>());
    }

    private static WardAssignmentViewDto LockedView(string title = "Algebra") =>
        new(AssignmentId, title, null, QuestionsUnlocked: false,
            Modules: new[]
            {
                new WardModuleViewDto(Module1, ModuleTypeDto.Guide, "Guide 1", "https://example.com/g1", 1, 100, true, 0, null),
                new WardModuleViewDto(Module2, ModuleTypeDto.Video, "Video 1", "https://example.com/v1", 2, 100, true, 0, null)
            });

    private static WardAssignmentViewDto UnlockedView(string title = "Algebra") =>
        new(AssignmentId, title, null, QuestionsUnlocked: true,
            Modules: new[]
            {
                new WardModuleViewDto(Module1, ModuleTypeDto.Guide, "Guide 1", "https://example.com/g1", 1, 100, true, 100, null),
                new WardModuleViewDto(Module2, ModuleTypeDto.Video, "Video 1", "https://example.com/v1", 2, 100, true, 100, null)
            });

    [TestMethod]
    public async Task Modules_Render_InOrder()
    {
        SetupView(LockedView());
        SetupResultNotFound();

        var cut = RenderAssignment();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Guide 1"));
        // DisplayOrder 1 (Guide) precedes DisplayOrder 2 (Video) in the markup.
        cut.Markup.IndexOf("Guide 1", StringComparison.Ordinal)
            .Should().BeLessThan(cut.Markup.IndexOf("Video 1", StringComparison.Ordinal));
    }

    [TestMethod]
    public void QuestionsLocked_Shows_GateHint()
    {
        SetupView(LockedView());
        SetupResultNotFound();
        var cut = RenderAssignment();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("unlock the questions"));
    }

    [TestMethod]
    public void QuestionsUnlocked_Shows_Submit()
    {
        SetupView(UnlockedView());
        SetupResultNotFound();
        var cut = RenderAssignment();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Submit answers"));
    }

    [TestMethod]
    public async Task Submit_Triggers_ClientCall()
    {
        SetupView(UnlockedView());
        SetupResultNotFound();
        var hit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{AssignmentId}/students/{StudentId}/submission")
            .Respond(HttpStatusCode.NoContent)
            .With(req => { hit.TrySetResult(true); return true; });

        var cut = RenderAssignment();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Submit answers"));
        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Submit", StringComparison.OrdinalIgnoreCase)).Click();

        var posted = await hit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        posted.Should().BeTrue("submitting should call the assignment submission endpoint");
    }

    [TestMethod]
    public void VideoModule_RecordsHeartbeatImport_ForVideoModule()
    {
        // The component attaches the watchers once a view with a video module
        // renders. JS itself is not executed (loose mode): what we verify is the
        // meaningful binding — the wardPlayer module import is requested.
        SetupView(UnlockedView());
        SetupResultNotFound();
        var cut = RenderAssignment();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Video 1"));

        cut.WaitForAssertion(() => JSInterop.Invocations.Should().Contain(i =>
            i.Identifier == "import" &&
            i.Arguments.Any(a => (string?)a == WardPlayerModulePath)));
    }

    [TestMethod]
    public void NoVideoModule_DoesNotRecordHeartbeatImport()
    {
        // A view with only guide modules must not request the wardPlayer module —
        // the heartbeat attach is gated on a video module being present.
        var guideOnly = new WardAssignmentViewDto(AssignmentId, "Algebra", null, QuestionsUnlocked: false,
            Modules: new[]
            {
                new WardModuleViewDto(Module1, ModuleTypeDto.Guide, "Guide 1", "https://example.com/g1", 1, 100, true, 100, null)
            });
        SetupView(guideOnly);
        SetupResultNotFound();
        var cut = RenderAssignment();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Guide 1"));

        JSInterop.Invocations.Should().NotContain(i =>
            i.Identifier == "import" &&
            i.Arguments.Any(a => (string?)a == WardPlayerModulePath));
    }

    [TestMethod]
    public void ResultView_Binds_ScoreAndPassState()
    {
        SetupView(UnlockedView());
        SetupResult(new SubmissionDetailDto(
            SubmissionId: Guid.NewGuid(),
            AssignmentId: AssignmentId,
            StudentId: StudentId,
            CurrentVersionNumber: 1,
            ReviewState: ReviewStateDto.Graded,
            LastSubmittedAt: DateTimeOffset.UtcNow,
            Versions: new[]
            {
                new SubmissionVersionDto(
                    Id: Guid.NewGuid(), VersionNumber: 1, Source: SubmissionSourceDto.Student,
                    Content: "ans", SubmittedByGuardianId: null, SubmittedAt: DateTimeOffset.UtcNow,
                    Score: 0.85m, Passed: true)
            },
            Review: null));

        var cut = RenderAssignment();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Result"));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("0.85"));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Passed"));
    }

    [TestMethod]
    public void LoadFailure_SurfacesError_NoPerpetualSpinner()
    {
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{AssignmentId}/students/{StudentId}/modules")
            .Respond(HttpStatusCode.InternalServerError, "application/json", "boom");

        var cut = RenderAssignment();
        // The failure surfaces the client's error message and the loading spinner is
        // not left spinning forever (an error bar replaces it rather than a perpetual ring).
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Internal Server Error"));
        cut.Markup.Should().NotContain("fluent-progress-ring");
    }

    [TestMethod]
    public void SubmitFailure_RestoresIdleButton_ShowsError()
    {
        SetupView(UnlockedView());
        SetupResultNotFound();
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{AssignmentId}/students/{StudentId}/submission")
            .Respond(HttpStatusCode.InternalServerError, "application/json", "boom");

        var cut = RenderAssignment();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Submit answers"));
        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Submit", StringComparison.OrdinalIgnoreCase)).Click();

        // ar-9 fix class: a failed mutation must not leave the button stuck on
        // "Submitting…" — the busy state restores to idle and an error surface is shown.
        cut.WaitForAssertion(() => cut.Markup.Should().NotContain("Submitting"));
        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Submit", StringComparison.OrdinalIgnoreCase))
            .TextContent.Trim().Should().Be("Submit answers");
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Internal Server Error"));
    }

    [TestMethod]
    public void MissingIdentifiers_ShowsError_NoPerpetualSpinner()
    {
        // Route params resolve to Guid.Empty (no student/assignment identifier): the
        // page must surface a user-visible error instead of a perpetual spinner.
        var cut = Render<SchoolCollab.Families.Components.Pages.Ward.Assignment>();
        cut.Markup.Should().Contain("Missing assignment or ward identifier");
        cut.Markup.Should().NotContain("fluent-progress-ring");
    }

    [TestMethod]
    public void BackNavigation_PointsAtWardList()
    {
        // The player renders an in-page back-navigation link to the ward list route
        // (/ward/{StudentId}) so the user is never stranded on the deep player route.
        SetupView(UnlockedView());
        SetupResultNotFound();
        var cut = RenderAssignment();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Video 1"));

        cut.FindAll("fluent-anchor")
            .Any(a => a.GetAttribute("href") == $"/ward/{StudentId}")
            .Should().BeTrue("the player must render a back link targeting the ward list");
    }

    private void SetupResult(SubmissionDetailDto detail)
    {
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{AssignmentId}/students/{StudentId}/submission")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(detail, JsonSubmission));
    }

    private void SetupView(WardAssignmentViewDto view)
    {
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{AssignmentId}/students/{StudentId}/modules")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(view, Json));
    }

    private void SetupResultNotFound()
    {
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{AssignmentId}/students/{StudentId}/submission")
            .Respond(HttpStatusCode.NotFound);
    }

    private IRenderedComponent<SchoolCollab.Families.Components.Pages.Ward.Assignment> RenderAssignment() =>
        Render<SchoolCollab.Families.Components.Pages.Ward.Assignment>(parameters =>
        {
            parameters.Add(a => a.AssignmentId, AssignmentId);
            parameters.Add(a => a.StudentId, StudentId);
        });
}
