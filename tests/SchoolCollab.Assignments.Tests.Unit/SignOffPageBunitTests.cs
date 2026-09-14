using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;
using SignOffPage = SchoolCollab.Assignments.Application.Components.Pages.Assignments.SignOff;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-C2 guardian e-sign page (spec §3.2 line 50) — the round-doc binding list:
/// <list type="bullet">
///   <item><c>Sign_GatedByConsentCheckbox</c> — the consent checkbox gates the Sign button.</item>
///   <item><c>Sign_TypedFlow_CallsClientWithTypedName</c> — typed signature rides the POST body.</item>
///   <item><c>Sign_ClickFlow_CallsClientWithNullName</c> — click signature sends a null typed name.</item>
///   <item><c>AlreadySigned_RendersReadOnlyView</c> — a signed visit is a view, not a 409 page.</item>
///   <item><c>GuardianSelect_Required</c> — the acting guardian must be self-selected (v1 posture).</item>
///   <item><c>ServerError_ShowsMessageBar</c> — a failed POST surfaces the error bar.</item>
/// </list>
/// Fields are seeded the way the page's own UI would (bUnit + reflection, the
/// AssignmentCreateBunitTests precedent) so the assertion targets the exact
/// <c>Disabled</c> expression the page computes.
/// </summary>
[TestClass]
public class SignOffPageBunitTests : BunitContext
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GuardianId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;

    public SignOffPageBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        _apiJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter<SignOffStateDto>(),
                new JsonStringEnumConverter<SignatureTypeDto>(),
            }
        };

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<SignOffPage>>());
    }

    private static string ContextUrl => $"/assignments/{AssignmentId}/students/{StudentId}/sign-off";

    private void SetupContext(SignOffStateDto state, params WardGuardianDto[] guardians)
    {
        var context = new SignOffContextDto(
            AssignmentId, StudentId, "Math HW", "Ward One", state,
            state == SignOffStateDto.Signed ? DateTimeOffset.UtcNow : null,
            null, 2, 88m, true, "Consent text shown to the guardian", guardians);
        _mockHttp.When(HttpMethod.Get, $"http://localhost{ContextUrl}")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(context, _apiJsonOptions));
    }

    private void SetupDefaultGuardians()
    {
        _mockHttp.When(HttpMethod.Get, $"http://localhost{ContextUrl}")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(
                Context(SignOffStateDto.AwaitingSignature, new WardGuardianDto(GuardianId, "Jane Doe", true)), _apiJsonOptions));
    }

    private static SignOffContextDto Context(SignOffStateDto state, params WardGuardianDto[] guardians) =>
        new(AssignmentId, StudentId, "Math HW", "Ward One", state, null, null, 2, 88m, true,
            "Consent text shown to the guardian", guardians);

    private static SignOffStatusDto StatusAfterSign() =>
        new(StudentId, "Ward One", GuardianId, "Jane Doe", GuardianId, "Jane Doe",
            SignOffStateDto.Signed, DateTimeOffset.UtcNow, null, true, true, 2, 88m, true);

    private IRenderedComponent<SignOffPage> RenderPage()
    {
        var cut = Render<SignOffPage>(parameters => parameters
            .Add(p => p.AssignmentId, AssignmentId)
            .Add(p => p.StudentId, StudentId));
        cut.WaitForAssertion(() => cut.FindAll("fluent-button").Should().NotBeEmpty());
        return cut;
    }

    private static void SetField<T>(IRenderedComponent<SignOffPage> cut, string field, T value) =>
        typeof(SignOffPage).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(cut.Instance, value);

    private static IElement SignButton(IRenderedComponent<SignOffPage> cut, string text = "Sign") =>
        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == text);

    [TestMethod]
    public void Sign_GatedByConsentCheckbox()
    {
        SetupDefaultGuardians();
        var cut = RenderPage();

        // Guardian + typed name supplied; consent still unchecked.
        SetField(cut, "_selectedGuardianId", (Guid?)GuardianId);
        SetField(cut, "_typedSignature", "Jane Doe");
        cut.Render();

        SignButton(cut).HasAttribute("disabled").Should().BeTrue("the consent checkbox has not been confirmed");

        SetField(cut, "_consentChecked", true);
        cut.Render();

        SignButton(cut).HasAttribute("disabled").Should().BeFalse("all gates are satisfied");
    }

    [TestMethod]
    public void Sign_TypedFlow_CallsClientWithTypedName()
    {
        SetupDefaultGuardians();
        var cut = RenderPage();

        SetField(cut, "_selectedGuardianId", (Guid?)GuardianId);
        SetField(cut, "_consentChecked", true);
        SetField(cut, "_signatureType", SignatureTypeDto.Typed);
        SetField(cut, "_typedSignature", "Jane Doe");
        cut.Render();

        string? capturedBody = null;
        _mockHttp.Expect(HttpMethod.Post, $"http://localhost{ContextUrl}")
            .With(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(StatusAfterSign(), _apiJsonOptions));

        SignButton(cut).Click();

        cut.WaitForAssertion(() => capturedBody.Should().NotBeNull());
        capturedBody.Should().Contain($"\"guardianId\":\"{GuardianId}\"");
        capturedBody.Should().Contain("\"signatureType\":\"Typed\"");
        capturedBody.Should().Contain("\"typedSignature\":\"Jane Doe\"");
    }

    [TestMethod]
    public void Sign_ClickFlow_CallsClientWithNullName()
    {
        SetupDefaultGuardians();
        var cut = RenderPage();

        SetField(cut, "_selectedGuardianId", (Guid?)GuardianId);
        SetField(cut, "_consentChecked", true);
        SetField(cut, "_signatureType", SignatureTypeDto.Click);
        cut.Render();

        string? capturedBody = null;
        _mockHttp.Expect(HttpMethod.Post, $"http://localhost{ContextUrl}")
            .With(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(StatusAfterSign(), _apiJsonOptions));

        SignButton(cut, "Click to sign").Click();

        cut.WaitForAssertion(() => capturedBody.Should().NotBeNull());
        capturedBody.Should().Contain("\"signatureType\":\"Click\"");
        capturedBody.Should().Contain("\"typedSignature\":null");
    }

    [TestMethod]
    public void AlreadySigned_RendersReadOnlyView()
    {
        SetupContext(SignOffStateDto.Signed, new WardGuardianDto(GuardianId, "Jane Doe", true));

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("completed", "a signed visit renders the read-only confirmation");
            cut.FindAll("fluent-button").Should().NotContain(b => b.TextContent.Trim() == "Sign");
        });
    }

    [TestMethod]
    public void GuardianSelect_Required()
    {
        SetupDefaultGuardians();
        var cut = RenderPage();

        // Consent + typed name supplied; no acting guardian selected.
        SetField(cut, "_consentChecked", true);
        SetField(cut, "_typedSignature", "Jane Doe");
        cut.Render();

        SignButton(cut).HasAttribute("disabled").Should().BeTrue("the acting guardian must be self-selected");
    }

    [TestMethod]
    public void ServerError_ShowsMessageBar()
    {
        SetupDefaultGuardians();
        var cut = RenderPage();

        SetField(cut, "_selectedGuardianId", (Guid?)GuardianId);
        SetField(cut, "_consentChecked", true);
        SetField(cut, "_typedSignature", "Jane Doe");
        cut.Render();

        _mockHttp.Expect(HttpMethod.Post, $"http://localhost{ContextUrl}")
            .Respond(HttpStatusCode.InternalServerError);

        SignButton(cut).Click();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("500", "the failed POST surfaces the error bar"));
    }
}
