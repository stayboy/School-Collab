using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Families.Components.Pages.Ward;
using SchoolCollab.Families.Services;

namespace SchoolCollab.Families.Tests.Unit;

/// <summary>
/// WS-F3 (ar-15-signoff-relocation, decisions (b)/(c)/(e)) — the Guardian Families-hosted
/// sign page. Binding coverage: context render; the typed + click sign flows through the
/// guardian route (the body carries NO GuardianId — the signing identity is token-bound);
/// the certificate affordance (post-finalize); the already-signed/404/409 re-entry
/// posture; and the NO-self-select invariant (decision (b) — no guardian picker, no
/// GuardianId is ever surfaced). New behavioural code in the page zero-tested before this.
/// </summary>
[TestClass]
public class GuardianSignOffPageTests : BunitContext
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AssignmentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid StudentId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly MockHttpMessageHandler _mockHttp;

    public GuardianSignOffPageTests()
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
        Services.AddSingleton(Mock.Of<ILogger<GuardianCertificateDownloadService>>());
        Services.AddSingleton(Mock.Of<ILogger<SignOff>>());
        Services.AddScoped<GuardianCertificateDownloadService>();
        // The acting guardian comes from the ar-14 landing cookie principal (tenant_id +
        // contact_id) — the pair the page re-mints into the x-deeplink-token header. None
        // present ⇒ no signer (the page's error posture, not a render crash).
        Services.AddScoped<AuthenticationStateProvider>(_ => new FakeAuthState(ContactId, TenantId));
    }

    [TestMethod]
    public void Context_Renders_TitleAndNoGuardianSelect()
    {
        SetupContext(SampleContext(state: SignOffStateDto.AwaitingSignature));

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Assignment One"));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("consent text"));

        // decision (b) no-self-select invariant: no guardian picker dropdown (the sign
        // page has no FluentSelect / guardian selector — the identity is token-bound).
        cut.Markup.Should().NotContain("fluent-select");
    }

    [TestMethod]
    public async Task TypedSign_PostsViaGuardianRoute_BodyCarriesNoGuardianId()
    {
        // POST matcher first and method-specific, then the GET sequence: after the sign POST
        // the page reloads the context → the already-signed view. This variant also captures
        // the request body so the test can prove it carries no guardianId.
        SetupSignOkCapture();
        SetupContextThenSignedAfterSign(
            SampleContext(state: SignOffStateDto.AwaitingSignature),
            SampleContext(state: SignOffStateDto.Signed));

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Type your full name"));

        // bUnit + FluentUI: the web components dispatch FluentUI-typed args (oncheckedchange
        // carries CheckboxChangeEventArgs, not ChangeEventArgs), so drive the BOUND callbacks
        // directly — the repo's FluentCheckbox/InputBase bUnit pattern — rather than
        // synthesising web-component events. This exercises the page's real bind path.
        await cut.InvokeAsync(() => cut.FindComponent<FluentTextField>().Instance.ValueChanged.InvokeAsync("Jane Doe"));
        await cut.InvokeAsync(() => cut.FindComponent<FluentCheckbox>().Instance.ValueChanged.InvokeAsync(true));

        var sign = cut.FindAll("fluent-button")
            .Single(b => b.TextContent.Contains("Sign", StringComparison.OrdinalIgnoreCase));
        sign.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("This sign-off has been completed"));

        // The successful POST + reload proves the page reached the signed view through the
        // guardian route. Decision (b): the signing identity is token-bound, so the body the
        // page actually issued must carry no guardian id at all (no self-select, no spoofing).
        _signBody.Should().NotBeNullOrWhiteSpace("the sign page must have issued the POST");
        _signBody.Should().NotContainEquivalentOf("guardianId");
    }

    [TestMethod]
    public async Task ClickSign_PostsAndShowsSignedView()
    {
        SetupSignOk().Respond(HttpStatusCode.OK, "application/json",
            JsonSerializer.Serialize(SampleStatus(), Json));
        SetupContextThenSignedAfterSign(
            SampleContext(state: SignOffStateDto.AwaitingSignature),
            SampleContext(state: SignOffStateDto.Signed));

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Click to sign"));

        // Drive the bound callbacks (FluentUI-typed events; see the typed-sign test).
        await cut.InvokeAsync(() => cut.FindComponent<FluentRadioGroup<SignatureTypeDto>>().Instance.ValueChanged.InvokeAsync(SignatureTypeDto.Click));
        await cut.InvokeAsync(() => cut.FindComponent<FluentCheckbox>().Instance.ValueChanged.InvokeAsync(true));

        var sign = cut.FindAll("fluent-button")
            .Single(b => b.TextContent.Contains("Click to sign", StringComparison.OrdinalIgnoreCase));
        sign.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("This sign-off has been completed"));
    }

    [TestMethod]
    public void CertificateAffordance_Renders_OnlyAfterFinalize()
    {
        SetupContext(SampleContext(state: SignOffStateDto.Signed, finalizedAt: DateTimeOffset.UtcNow));

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Download certificate"));
        cut.Markup.Should().NotContain("Type your full name");
    }

    [TestMethod]
    public void AlreadySigned_ReEntry_RendersReadOnly_NoSignButton()
    {
        SetupContext(SampleContext(state: SignOffStateDto.Signed));

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("This sign-off has been completed"));
        cut.Markup.Should().NotContain("fluent-button");                       // no sign affordance
        cut.Markup.Should().NotContain("Type your full name");
    }

    [TestMethod]
    public void NotFound_ShowsMessage_NoPerpetualSpinner()
    {
        _mockHttp.When(GuardianContextUrl()).Respond(HttpStatusCode.NotFound);

        var cut = RenderPage();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Assignment or student not found."));
        cut.Markup.Should().NotContain("fluent-progress-ring");
    }

    [TestMethod]
    public void ExpiredLink_SurfacesTheReEntryMessage()
    {
        // 401 (expired/revoked link) → the page's explicit re-open-from-email message,
        // not a crash or a perpetual spinner.
        _mockHttp.When(GuardianContextUrl()).Respond(HttpStatusCode.Unauthorized);

        var cut = RenderPage();
        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("This sign-off link is no longer valid. Please open it again from the link in your email."));
    }

    private MockedRequest SetupContext(SignOffContextDto ctx) =>
        _mockHttp.When(HttpMethod.Get, GuardianContextUrl()).Respond(HttpStatusCode.OK, "application/json",
            JsonSerializer.Serialize(ctx, Json));

    /// <summary>GET-hit counter — the sign flow reloads the context after a successful
    /// POST, so call 1 renders the awaiting view and call 2+ the signed view. Every
    /// matcher here is method-specific: a method-agnostic <c>When(url)</c> matcher would
    /// be picked first for the POST too (MockHttp returns the first match), swallowing
    /// the sign call and leaving the page on the awaiting view.</summary>
    private int _contextGetCount;

    private MockedRequest SetupContextThenSignedAfterSign(SignOffContextDto before, SignOffContextDto after) =>
        _mockHttp.When(HttpMethod.Get, GuardianContextUrl())
            .Respond(_ =>
            {
                var ctx = Interlocked.Increment(ref _contextGetCount) == 1 ? before : after;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(ctx, Json),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });

    private MockedRequest SetupSignOk() =>
        _mockHttp.When(HttpMethod.Post, $"http://localhost/guardian/assignments/{AssignmentId}/students/{StudentId}/sign-off");

    /// <summary>The sign POST body the page actually issued — the typed-sign test asserts it
    /// carries no guardianId (decision (b): the acting guardian is token-resolved server-side).</summary>
    private string? _signBody;

    private MockedRequest SetupSignOkCapture() =>
        _mockHttp.When(HttpMethod.Post, $"http://localhost/guardian/assignments/{AssignmentId}/students/{StudentId}/sign-off")
            .Respond(async req =>
            {
                _signBody = req.Content is null
                    ? null
                    : await req.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(SampleStatus(), Json),
                        System.Text.Encoding.UTF8, "application/json")
                };
            });

    private string GuardianContextUrl() =>
        $"http://localhost/guardian/assignments/{AssignmentId}/students/{StudentId}/sign-off";

    private IRenderedComponent<SignOff> RenderPage() =>
        Render<SignOff>(parameters =>
        {
            parameters.Add(p => p.AssignmentId, AssignmentId);
            parameters.Add(p => p.StudentId, StudentId);
        });

    private static SignOffContextDto SampleContext(SignOffStateDto state, DateTimeOffset? finalizedAt = null) =>
        new(AssignmentId, StudentId, "Assignment One", "Ward One", state,
            SignedAt: state == SignOffStateDto.Signed ? DateTimeOffset.UtcNow : null,
            finalizedAt, CurrentVersionNumber: 2, Score: 88m, Passed: true,
            ConsentText: "consent text", Guardians: [new WardGuardianDto(Guid.NewGuid(), "Jane Doe", true)]);

    private static SignOffStatusDto SampleStatus() =>
        new(StudentId, "Ward One", Guid.NewGuid(), "Jane Doe", Guid.NewGuid(), "Jane Doe",
            SignOffStateDto.Signed, DateTimeOffset.UtcNow, null, true, true, 2, 88m, true);

    /// <summary>A test auth state returning the guardian cookie-principal pair.</summary>
    private sealed class FakeAuthState(Guid contactId, Guid tenantId) : AuthenticationStateProvider
    {
        private readonly Task<AuthenticationState> _task = Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim("contact_id", contactId.ToString()),
                    new Claim("tenant_id", tenantId.ToString())
                },
                "test"))));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => _task;
    }
}
