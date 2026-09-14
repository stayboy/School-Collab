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
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-C1 teacher surface (spec §3.2 line 51) — the <c>SignOffSection</c> card on
/// the Assignments Detail page. Covers the round-doc binding list:
/// <list type="bullet">
///   <item><c>RendersNothing_WhenRequiresSignatureFalse</c> — the render gate.</item>
///   <item><c>RendersRows_WithStatusBadges</c> — per-ward rows + status labels.</item>
///   <item><c>Reassign_OpensDialog_AndCallsClient</c> — dialog result → POST reassign → reload.</item>
///   <item><c>Finalize_CallsClient_AndReloads</c> — POST finalize → reload.</item>
///   <item><c>Busy_ResetsInFinally</c> — the ar-8 UI-fix regression class: a failed load clears the spinner.</item>
/// </list>
/// Same Blazor + MockHttp + bUnit pattern as <c>AssignmentDetailBunitTests</c>;
/// the dialog is mocked at <c>ShowDialogAsync&lt;ReassignSignerDialog,
/// DialogShellData&lt;ReassignSignerFormModel&gt;&gt;</c> — exactly what
/// <c>ShowShellDialogAsync</c> resolves to.
/// </summary>
[TestClass]
public class SignOffSectionBunitTests : BunitContext
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StudentId2 = Guid.Parse("22222222-2222-2222-2222-222222222223");
    private static readonly Guid GuardianId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid NewGuardianId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;
    private int _statusesGetCount;
    private int _contextGetCount;

    public SignOffSectionBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        _apiJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter<SignOffStateDto>(),
            }
        };

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<SignOffSection>>());
    }

    private static SignOffStatusDto Row(
        SignOffStateDto state,
        DateTimeOffset? finalizedAt = null,
        Guid? studentId = null) =>
        new(
            studentId ?? StudentId,
            "Ward One",
            GuardianId,
            "Jane Doe",
            state == SignOffStateDto.Signed ? GuardianId : null,
            state == SignOffStateDto.Signed ? "Jane Doe" : null,
            state,
            state == SignOffStateDto.Signed ? DateTimeOffset.UtcNow : null,
            finalizedAt,
            Delivered: true,
            Opened: true,
            CurrentVersionNumber: 2,
            Score: 88m,
            Passed: true);

    private void SetupStatuses(params SignOffStatusDto[] rows)
    {
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{AssignmentId}/sign-off-statuses")
            .Respond(_ =>
            {
                _statusesGetCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(rows, _apiJsonOptions), System.Text.Encoding.UTF8, "application/json")
                };
            });
    }

    private void SetupContext(params WardGuardianDto[] guardians)
    {
        var context = new SignOffContextDto(
            AssignmentId, StudentId, "Math HW", "Ward One",
            SignOffStateDto.AwaitingSignature, null, null, 2, 88m, true,
            "Consent text", guardians);
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{AssignmentId}/students/{StudentId}/sign-off")
            .Respond(_ =>
            {
                _contextGetCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(context, _apiJsonOptions),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });
    }

    /// <summary>Replaces the FluentUI <see cref="IDialogService"/> with the mock
    /// (GetRequiredService returns the FIRST registration — remove, don't shadow).</summary>
    private Mock<IDialogService> SetupReassignDialogResult(ReassignSignerResult? result)
    {
        var dialogRef = new Mock<IDialogReference>();
        var dialogResult = result is null
            ? DialogResult.Cancel()
            : DialogResult.Ok<object?>(new DialogShellResult<ReassignSignerResult>(result));
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(dialogResult));

        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<ReassignSignerDialog, DialogShellData<ReassignSignerFormModel>>(
                It.IsAny<DialogShellData<ReassignSignerFormModel>>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);

        var existing = Services.Where(s => s.ServiceType == typeof(IDialogService)).ToList();
        foreach (var s in existing) Services.Remove(s);
        Services.AddSingleton(dialogMock.Object);
        return dialogMock;
    }

    private IRenderedComponent<SignOffSection> RenderSection(bool requiresSignature = true) =>
        Render<SignOffSection>(parameters => parameters
            .Add(p => p.AssignmentId, AssignmentId)
            .Add(p => p.RequiresSignature, requiresSignature));

    [TestMethod]
    public void RendersNothing_WhenRequiresSignatureFalse()
    {
        RenderSection(requiresSignature: false);

        // No self-load fires when the gate is closed (LoadAsync early-returns).
        _statusesGetCount.Should().Be(0, "the section must not load statuses when the assignment does not require a signature");
    }

    [TestMethod]
    public void RendersRows_WithStatusBadges()
    {
        SetupStatuses(
            Row(SignOffStateDto.AwaitingSignature),
            // Distinct StudentId — SignOffSection keys rows by row.StudentId; two
            // rows sharing one id would collide on @key (the CI failure on #231).
            Row(SignOffStateDto.Signed, finalizedAt: DateTimeOffset.UtcNow, studentId: StudentId2));

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Ward One");
            cut.Markup.Should().Contain("Awaiting signature");
            cut.Markup.Should().Contain("Finalized");
            cut.Markup.Should().Contain("Jane Doe");
            cut.Markup.Should().Contain("Open sign-off page");
        });
    }

    [TestMethod]
    public async Task Reassign_OpensDialog_AndCallsClient()
    {
        SetupStatuses(Row(SignOffStateDto.AwaitingSignature));
        SetupContext(new WardGuardianDto(GuardianId, "Jane Doe", true), new WardGuardianDto(NewGuardianId, "John Doe", false));
        var dialogMock = SetupReassignDialogResult(new ReassignSignerResult(NewGuardianId));

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-button").Should().Contain(b => b.TextContent.Trim() == "Reassign signer"));

        var expectedJson = JsonSerializer.Serialize(new ReassignSignOffRequest(NewGuardianId), _apiJsonOptions);
        string? postedBody = null;
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{AssignmentId}/students/{StudentId}/sign-off/reassign")
            .Respond(req =>
            {
                postedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

        await cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Reassign signer").ClickAsync();

        cut.WaitForAssertion(() =>
        {
            dialogMock.Verify(
                d => d.ShowDialogAsync<ReassignSignerDialog, DialogShellData<ReassignSignerFormModel>>(
                    It.IsAny<DialogShellData<ReassignSignerFormModel>>(), It.IsAny<DialogParameters>()),
                Times.Once,
                "the reassign dialog opens with the ward's guardians");
            _statusesGetCount.Should().BeGreaterThanOrEqualTo(2, "the card reloads after a successful reassign");
            postedBody.Should().Be(expectedJson, "markup: {0}", cut.Markup);
        });
    }

    [TestMethod]
    public async Task Reassign_DialogCancelled_PostsNothing()
    {
        SetupStatuses(Row(SignOffStateDto.AwaitingSignature));
        SetupContext(new WardGuardianDto(NewGuardianId, "John Doe", false));
        var dialogMock = SetupReassignDialogResult(result: null);

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-button").Should().Contain(b => b.TextContent.Trim() == "Reassign signer"));

        // Any POST would be a bug — a plain recorder (not an Expect) so the request
        // count is observable without failing on an unmatched handler.
        var postCount = 0;
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{AssignmentId}/students/{StudentId}/sign-off/reassign")
            .Respond(_ => { postCount++; return new HttpResponseMessage(HttpStatusCode.NoContent); });

        await cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Reassign signer").ClickAsync();

        cut.WaitForAssertion(() => dialogMock.Invocations.Count.Should().BeGreaterThan(0));
        _contextGetCount.Should().BeGreaterThan(0, "the dialog needs the ward guardians from the context aggregate");
        postCount.Should().Be(0, "a cancelled dialog must not reassign");
        _statusesGetCount.Should().Be(1, "a cancelled dialog must not reload the card");
    }

    [TestMethod]
    public void Finalize_CallsClient_AndReloads()
    {
        SetupStatuses(Row(SignOffStateDto.Signed));

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-button").Should().Contain(b => b.TextContent.Trim() == "Finalize"));

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost/assignments/{AssignmentId}/students/{StudentId}/sign-off/finalize")
            .Respond(HttpStatusCode.NoContent);

        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Finalize").Click();

        cut.WaitForAssertion(() =>
        {
            _mockHttp.VerifyNoOutstandingExpectation();
            _statusesGetCount.Should().BeGreaterThanOrEqualTo(2, "the card reloads after a successful finalize");
        });
    }

    [TestMethod]
    public void Busy_ResetsInFinally()
    {
        // The ar-8 UI-fix regression class: a failing load must clear _busy (no
        // stuck spinner) and surface the error bar instead.
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{AssignmentId}/sign-off-statuses")
            .Respond(HttpStatusCode.InternalServerError);

        var cut = RenderSection();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("500", "the failure surfaces as the error bar");
            cut.FindAll("fluent-progress-ring").Should().BeEmpty("_busy must reset in finally");
        });
    }
}
