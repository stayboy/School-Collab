using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Pages.Teachers;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit smoke tests for the TeacherSetupWizard.
///
/// NOTE: FluentWizard only server-renders its chrome (the stepper is an empty
/// <c>&lt;ol&gt;</c> and the step panels are JS-composed into
/// <c>fluent-wizard-content</c>), so the profile-step fields are NOT present
/// in bUnit markup. Field-level assertions (Title / Gender / Date of birth /
/// Level of education / Qualifications present; legacy Email / Phone removed)
/// are therefore guarded at the CQRS/DTO layer (see TeacherCqrsTests, which
/// drives CreateTeacher/UpdateTeacher without email/phone). These tests
/// assert the page renders without a tenant/load crash and exposes the
/// wizard's Cancel / Continue actions.
/// </summary>
[TestClass]
public class TeacherSetupWizardBunitTests : BunitContext
{
    public TeacherSetupWizardBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private static ClaimsPrincipal CreateUser(bool realTenant)
    {
        var tenantId = realTenant ? Guid.NewGuid().ToString() : Guid.Empty.ToString();
        var claims = new[] { new Claim("tenant_id", tenantId), new Claim("tenant_name", realTenant ? "Hydeson" : "System") };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestScheme"));
    }

    private sealed class MutableAuthenticationStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal _user = new();
        public ClaimsPrincipal User { set { _user = value; NotifyAuthenticationStateChanged(GetAuthenticationStateAsync()); } }
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_user));
    }

    private void Register()
    {
        var auth = new MutableAuthenticationStateProvider { User = CreateUser(realTenant: true) };
        var http = new HttpClient(new ScriptedHandler()) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));
    }

    [TestMethod]
    public void Create_RendersWizardChrome_AndActionButtons()
    {
        Register();

        var cut = Render<TeacherSetupWizard>();

        cut.WaitForAssertion(() => cut.FindAll(".fluent-wizard").Should().NotBeEmpty());
        cut.Markup.Should().Contain("Cancel");
        cut.Markup.Should().Contain("Continue");
    }
}
