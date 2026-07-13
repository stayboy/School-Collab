using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels;
using SchoolCollab.Students.Admin.Services;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the tenancy gating of the grade-level wizard's
/// "Override name" button and "Reset to default" link.
///
/// <para>The override actions on both the grade and subject sections are
/// only meaningful for real tenants — in default-tenant mode the override
/// handler rewrites the global <c>CodedValue</c> directly, so the
/// per-tenant UI is meaningless. The wizard hides both controls when
/// the authenticated user's <c>tenant_id</c> claim is empty/default.</para>
/// </summary>
[TestClass]
public class GradeLevelWizardTenancyTests : BunitContext
{
    private static readonly Guid TestCodedValueId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string OverrideNameButtonText = "Override Name";
    private const string NewGradeButtonText = "New grade";

    public GradeLevelWizardTenancyTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    /// <summary>
    /// Mutable authentication state provider so each test can opt into either the
    /// "default" tenant (no real tenant, override rewrites the global
    /// blueprint) or a real tenant (override creates per-tenant rows).
    /// </summary>
    private sealed class MutableAuthenticationStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal _user = new ClaimsPrincipal();

        public ClaimsPrincipal User
        {
            get => _user;
            set
            {
                _user = value;
                NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
            }
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(_user));
    }

    /// <summary>
    /// Test HTTP message handler that returns appropriate responses for the
    /// wizard's initial loads. Only the coded-value GET needs to return a
    /// real payload (when the test simulates a grade selection); the
    /// period and students lists can return empty arrays.
    /// </summary>
    private sealed class WizardHttpHandler : HttpMessageHandler
    {
        public HttpResponseMessage RespondTo(HttpRequestMessage request)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            // GET /api/coded-values/{id} — returns the picked coded value
            if (path.StartsWith("/api/coded-values/", StringComparison.OrdinalIgnoreCase)
                && HttpMethod.Get.Equals(request.Method))
            {
                var cv = new CodedValueDto(
                    Id: TestCodedValueId,
                    Code: "GRADE_1",
                    Name: "Grade 1",
                    Description: null,
                    ParentId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    ParentCode: "GRADE",
                    IsDisabled: false,
                    DisplayOrder: 1,
                    CreatedAt: DateTimeOffset.UtcNow,
                    UpdatedAt: DateTimeOffset.UtcNow,
                    Attributes: [],
                    AttributeDefinitions: [],
                    ChildrenCount: 0,
                    IsDeleted: false,
                    DeletedAt: null,
                    IsOverridden: false);
                return Json(HttpStatusCode.OK, cv);
            }

            // GET /api/coded-values?parentCode=... — returns the GRADE children
            if (path.StartsWith("/api/coded-values", StringComparison.OrdinalIgnoreCase)
                && HttpMethod.Get.Equals(request.Method))
            {
                return Json(HttpStatusCode.OK, Array.Empty<CodedValueDto>());
            }

            // GET /students/periods — return one Active period so the wizard's
            // "Open a term" entry gate stays hidden and its steps (override /
            // new-grade buttons) render. A real tenant must have an open term
            // before using the wizard (FR-A6).
            if (path.Contains("/students/periods", StringComparison.OrdinalIgnoreCase))
            {
                var activePeriod = new PeriodDto(
                    Id: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    Name: "2025/2026",
                    StartDate: new DateOnly(2025, 9, 1),
                    EndDate: new DateOnly(2026, 8, 31),
                    Status: "Active",
                    NextPeriodId: null,
                    CreatedAt: DateTimeOffset.UtcNow,
                    UpdatedAt: DateTimeOffset.UtcNow);
                return Json(HttpStatusCode.OK, new[] { activePeriod });
            }

            // GET /students/students, /subjects, /grade-levels — empty arrays
            if (path.Contains("/students/students", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/students/subjects", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/students/grade-levels", StringComparison.OrdinalIgnoreCase))
            {
                return Json(HttpStatusCode.OK, Array.Empty<object>());
            }

            // Default: 404 so the test fails loudly if an unexpected call slips through
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unhandled request: {request.Method} {path}")
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(RespondTo(request));

        private static HttpResponseMessage Json<T>(HttpStatusCode status, T body) =>
            new(status) { Content = JsonContent.Create(body) };
    }

    private MutableAuthenticationStateProvider RegisterServices(bool realTenant)
    {
        var authProvider = new MutableAuthenticationStateProvider();
        if (realTenant)
        {
            var tenantId = Guid.NewGuid();
            authProvider.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("tenant_id", tenantId.ToString()),
                new Claim("tenant_name", "Hydeson"),
                new Claim("tenant_type", "School")
            }, "TestScheme"));
        }
        else
        {
            authProvider.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("tenant_id", Guid.Empty.ToString()),
                new Claim("tenant_name", "System"),
                new Claim("tenant_type", "Organization")
            }, "TestScheme"));
        }

        var handler = new WizardHttpHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };

        Services.AddSingleton<AuthenticationStateProvider>(authProvider);
        Services.AddSingleton(new CodedValuesApiClient(http));
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance));
        Services.AddSingleton(new VisibleTenantService(authProvider, NullLogger<VisibleTenantService>.Instance));

        return authProvider;
    }

    private static ClaimsPrincipal CreateUser(string tenantId, string tenantName, string tenantType) =>
        new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", tenantId),
            new Claim("tenant_name", tenantName),
            new Claim("tenant_type", tenantType)
        }, "TestScheme"));

    private IRenderedComponent<GradeLevelWizard> RenderWizard()
    {
        // Pass an empty HeaderActions render fragment so the wizard's @code
        // doesn't need a parent layout with a HeaderActions parameter.
        return Render<GradeLevelWizard>();
    }

    /// <summary>
    /// Finds the "Override name" button text in the rendered markup, or
    /// null if not present. The button may be inside a fluent-button with
    /// child text content, or as a fluent-anchor with that text.
    /// </summary>
    private static bool HasOverrideButton(IRenderedComponent<GradeLevelWizard> cut)
        => cut.Markup.Contains(OverrideNameButtonText, StringComparison.Ordinal);

    private static bool HasNewGradeButton(IRenderedComponent<GradeLevelWizard> cut)
        => cut.Markup.Contains(NewGradeButtonText, StringComparison.Ordinal);

    [TestMethod]
    public void OverrideNameButton_Visible_When_RealTenant()
    {
        // Arrange: real tenant in scope
        RegisterServices(realTenant: true);

        // Act: render the wizard
        var cut = RenderWizard();

        // The Override name button sits in the grade section's coded-value-actions
        // div, alongside the New grade button. It should render unconditionally
        // when a real tenant is in scope (even before the user picks a grade,
        // because the button is in the action bar above the picker — wait,
        // it's only enabled when _codedValueIdNullable.HasValue; but it
        // should still appear in the markup).
        //
        // Note: the wizard's step 1 layout puts the Override name button in
        // the 1st column above the grade confirmation card. The button is
        // disabled until a grade is picked, but it should be present in the
        // markup when a real tenant is in scope.
        HasNewGradeButton(cut).Should().BeTrue("New grade button is always present");
        HasOverrideButton(cut).Should().BeTrue("Override name button must be present when a real tenant is in scope");
    }

    [TestMethod]
    public void OverrideNameButton_Hidden_When_DefaultTenant()
    {
        // Arrange: default tenant (Guid.Empty) in scope — the override handler
        // rewrites the global CodedValue directly, so the per-tenant UI is
        // meaningless.
        RegisterServices(realTenant: false);

        // Act: render the wizard
        var cut = RenderWizard();

        // Assert: for a default tenant the wizard (and its New grade / Override
        // name buttons) is hidden and the tenant prompt is shown instead of
        // loading tenant-scoped data.
        HasNewGradeButton(cut).Should().BeFalse("the wizard is hidden for a default tenant");
        HasOverrideButton(cut).Should().BeFalse("Override name button must be hidden when the default tenant is in scope");
        cut.Markup.Should().Contain("You have no tenant assigned", "the tenant prompt must show for a default tenant");
    }

    [TestMethod]
    public void IsRealTenant_True_After_TenantSwitch_Real_To_Default()
    {
        // Arrange: start with a real tenant, render, verify override visible
        var authProvider = RegisterServices(realTenant: true);
        var cut = RenderWizard();
        HasOverrideButton(cut).Should().BeTrue("Override visible with real tenant");

        // Act: switch to the default tenant (simulates the dev tenant switcher
        // selecting "(default tenant)"). The wizard's OnInitializedAsync reads
        // the tenant from the auth state on the next render.
        authProvider.User = CreateUser(Guid.Empty.ToString(), "System", "Organization");
        cut.Render();

        // Assert: override is now hidden
        HasOverrideButton(cut).Should().BeFalse("Override must be hidden after switching to the default tenant");
    }

    [TestMethod]
    public void IsRealTenant_True_After_TenantSwitch_Default_To_Real()
    {
        // Arrange: start with a real tenant so the wizard mounts and loads the
        // active period at initialization. (Period data is loaded once, on mount;
        // switching tenants re-evaluates the real-tenant flag but does not reload
        // periods — so the active period must be present at mount.) This lets us
        // exercise the default -> real transition below.
        var authProvider = RegisterServices(realTenant: true);
        var cut = RenderWizard();
        HasOverrideButton(cut).Should().BeTrue("Override visible with real tenant");

        // Switch to the default tenant: override must hide.
        authProvider.User = CreateUser(Guid.Empty.ToString(), "System", "Organization");
        cut.Render();
        HasOverrideButton(cut).Should().BeFalse("Override must be hidden after switching to the default tenant");

        // Act + Assert: the default -> real transition reveals the override again
        // (the active period loaded at mount stays in scope).
        authProvider.User = CreateUser(Guid.NewGuid().ToString(), "Hydeson", "School");
        cut.Render();
        cut.WaitForState(() => HasOverrideButton(cut), TimeSpan.FromSeconds(2));
        HasOverrideButton(cut).Should().BeTrue("Override must be visible after switching to a real tenant");
    }

    [TestMethod]
    public void TenantId_Debug_TenantSwitch_Values()
    {
        // Arrange: create tenants with specific GUIDs for debugging. Start with a
        // REAL tenant so the wizard mounts and loads the active period at mount
        // (period data is loaded once, on mount; tenant switches re-evaluate the
        // real-tenant flag but do not reload periods).
        var realTenantId = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
        var defaultTenantId = Guid.Empty;
        var authProvider = new MutableAuthenticationStateProvider();

        authProvider.User = CreateUser(realTenantId.ToString(), "Hydeson", "School");

        var handler = new WizardHttpHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };

        Services.AddSingleton<AuthenticationStateProvider>(authProvider);
        Services.AddSingleton(new CodedValuesApiClient(http));
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance));
        Services.AddSingleton(new VisibleTenantService(authProvider, NullLogger<VisibleTenantService>.Instance));

        // Act: render with the real tenant
        var cut = RenderWizard();

        // Debug: verify tenant context at initial render
        var authState = authProvider.GetAuthenticationStateAsync().Result;
        var tenantIdClaim = authState.User.FindFirst("tenant_id")?.Value;
        tenantIdClaim.Should().Be(realTenantId.ToString(), "Initial tenant should be the real tenant GUID");
        HasOverrideButton(cut).Should().BeTrue("Override visible when TenantId is a real GUID");

        // Act: switch to the default tenant
        authProvider.User = CreateUser(defaultTenantId.ToString(), "System", "Organization");
        cut.Render();

        // Debug: verify tenant context after switch
        authState = authProvider.GetAuthenticationStateAsync().Result;
        tenantIdClaim = authState.User.FindFirst("tenant_id")?.Value;
        tenantIdClaim.Should().Be(defaultTenantId.ToString(), "TenantId should now be Guid.Empty");
        HasOverrideButton(cut).Should().BeFalse("Override hidden when TenantId is Guid.Empty");

        // Act: switch back to the real tenant — the override reappears (active
        // period loaded at mount stays in scope).
        authProvider.User = CreateUser(realTenantId.ToString(), "Hydeson", "School");
        cut.Render();

        // Debug: verify tenant context after switching back
        authState = authProvider.GetAuthenticationStateAsync().Result;
        tenantIdClaim = authState.User.FindFirst("tenant_id")?.Value;
        tenantIdClaim.Should().Be(realTenantId.ToString(), "TenantId should be the real tenant GUID again");
        cut.WaitForState(() => HasOverrideButton(cut), TimeSpan.FromSeconds(2));
        HasOverrideButton(cut).Should().BeTrue("Override visible again after switching back to a real tenant");
    }
}
