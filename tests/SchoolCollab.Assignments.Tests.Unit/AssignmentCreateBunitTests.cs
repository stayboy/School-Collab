using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using CreatePage = SchoolCollab.Assignments.Application.Components.Pages.Assignments.Create;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// bUnit + Moq + RichardSzalay.MockHttp coverage for the Create.razor
/// wizard's question-generation gate plumbing (FR-220 / decision (a) —
/// the Resources UI does not land this round, so the form model
/// half-attachment is the only attachment surface under test here).
///
/// Per the round-3 plan's binding coverage list:
/// <list type="bullet">
///   <item>QuestionGenerationGate truth table (pure methods, no render)</item>
///   <item>Default step-1 markup shows DisabledHint under Manual / TeacherGraded</item>
///   <item>Clicking the Digital and AutoGraded cards flips the hint to EnabledHint (FR-220 hint updates)</item>
/// </list>
///
/// The wizard uses FluentUI's <c>FluentWizard</c> web component, which
/// does not always render step content under bUnit. The gating
/// assertions are bound to the <see cref="QuestionGenerationGate"/>
/// constants and the <c>HintText</c> method (decision per plan:
/// do NOT sink time into wizard-navigation testing).
/// </summary>
[TestClass]
public class AssignmentCreateBunitTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;

    public AssignmentCreateBunitTests()
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
            }
        };

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton<StudentsApiClient>();
        // CodedValuesApiClient is required by StudentsApiClient's ctor;
        // Create.razor injects both. Register with the same HttpClient.
        Services.AddSingleton<SchoolCollab.Admin.Shared.Services.CodedValuesApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<StudentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<CreatePage>>());
    }

    private void SetupGradeLevels(params GradeLevelDto[] grades)
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/grade-levels")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(grades, _apiJsonOptions));
    }

    private void SetupActivityGroups(params ActivityGroupDto[] groups)
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/activity-groups*")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(groups, _apiJsonOptions));
    }

    [TestMethod]
    public void QuestionGenerationGate_IsEnabled_TruthTable_FR220()
    {
        // Enabled: Digital/SemiManual + AutoGraded/InstantGraded
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Digital, GradingFormatDto.AutoGraded).Should().BeTrue();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Digital, GradingFormatDto.InstantGraded).Should().BeTrue();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.SemiManual, GradingFormatDto.AutoGraded).Should().BeTrue();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.SemiManual, GradingFormatDto.InstantGraded).Should().BeTrue();

        // Manual any: never enabled
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Manual, GradingFormatDto.AutoGraded).Should().BeFalse();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Manual, GradingFormatDto.InstantGraded).Should().BeFalse();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Manual, GradingFormatDto.TeacherGraded).Should().BeFalse();

        // Any type with TeacherGraded: never enabled
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Digital, GradingFormatDto.TeacherGraded).Should().BeFalse();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.SemiManual, GradingFormatDto.TeacherGraded).Should().BeFalse();

        // Null assignment type: never enabled
        QuestionGenerationGate.IsEnabled(null, GradingFormatDto.AutoGraded).Should().BeFalse();
    }

    [TestMethod]
    public void QuestionGenerationGate_HintText_TracksGate()
    {
        QuestionGenerationGate.HintText(AssignmentTypeDto.Digital, GradingFormatDto.AutoGraded)
            .Should().Be(QuestionGenerationGate.EnabledHint);
        QuestionGenerationGate.HintText(AssignmentTypeDto.Manual, GradingFormatDto.TeacherGraded)
            .Should().Be(QuestionGenerationGate.DisabledHint);
    }

    [TestMethod]
    public void QuestionGenerationGate_DisabledTooltip_HasStableCopy()
    {
        // The wizard + the bUnit tests both bind to this constant so the
        // wording cannot drift.
        QuestionGenerationGate.DisabledTooltip.Should().Contain("Auto Scored");
    }

    [TestMethod]
    public void Create_DefaultMarkup_ShowsDisabledHint_UnderManualTeacherGraded()
    {
        SetupGradeLevels();
        SetupActivityGroups();

        var cut = Render<CreatePage>();

        // Default state: Manual + TeacherGraded → gate closed → DisabledHint
        // The hint is a <p class="wizard-hint"> so the string is rendered
        // somewhere in the markup (we don't depend on a specific
        // FluentWizard step being visible — see plan fallback).
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain(QuestionGenerationGate.DisabledHint);
        }, TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void Create_HintText_ReflectsSelectionViaGate()
    {
        // Unit-style binding to the gate's own state machine rather than
        // driving the FluentWizard step UI (FluentWizard step content
        // does not reliably render under bUnit — see plan fallback).
        // The wizard's step-1 hint binds to QuestionGenerationGate.HintText
        // directly, so verifying the gate's outputs under every cell
        // proves the hint will follow.
        foreach (AssignmentTypeDto type in Enum.GetValues<AssignmentTypeDto>())
        {
            foreach (GradingFormatDto grading in Enum.GetValues<GradingFormatDto>())
            {
                var enabled = QuestionGenerationGate.IsEnabled(type, grading);
                var hint = QuestionGenerationGate.HintText(type, grading);
                if (enabled)
                {
                    hint.Should().Be(QuestionGenerationGate.EnabledHint,
                        "an enabled gate must show the EnabledHint");
                }
                else
                {
                    hint.Should().Be(QuestionGenerationGate.DisabledHint,
                        "a disabled gate must show the DisabledHint");
                }
            }
        }
    }
}
