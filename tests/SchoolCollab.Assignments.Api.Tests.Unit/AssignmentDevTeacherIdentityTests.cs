using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Api.Auth;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Round <c>teacher-scope-auth</c> D5 (Q5) — the dev bypass's <c>teacher_id</c> claim, configurable
/// through <c>TestAuth:TeacherId</c>.
///
/// <para><b>Regression guard, not a discriminator</b> (the plan's acceptance criterion 6 says so
/// explicitly): the claim-emitting/omitting leg already existed in
/// <c>TestAuthHandler</c>; what this file pins is that the NEW binding reaches the handler's own
/// options instance — claim-less by default (the CI posture), honoured when
/// <c>TestAuth:TeacherId</c> is configured.</para>
/// </summary>
[TestClass]
public class AssignmentDevTeacherIdentityTests
{
    private static readonly Guid DevTeacherId = Guid.Parse("00000000-0000-0000-0000-0000000000d7");

    private static IOptionsMonitor<TestAuthHandlerOptions> OptionsFor(string? configuredTeacherId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AssignmentDevTeacherIdentity.TeacherIdConfigKey] = configuredTeacherId,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAssignmentDevTeacherIdentity(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<TestAuthHandlerOptions>>();
    }

    /// <summary>Runs the REAL handler with the options the binding produced, and reports whether a
    /// <c>teacher_id</c> claim was emitted.</summary>
    private static async Task<bool> EmitsTeacherIdClaimAsync(IOptionsMonitor<TestAuthHandlerOptions> options)
    {
        var httpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        var scheme = new AuthenticationScheme(
            TestAuthExtensions.TestAuthScheme, TestAuthExtensions.TestAuthScheme, typeof(TestAuthHandler));

        var handler = new TestAuthHandler(options, LoggerFactory.Create(_ => { }), UrlEncoder.Default);
        await handler.InitializeAsync(scheme, httpContext);
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        return result.Principal!.FindFirst("teacher_id") is not null;
    }

    [TestMethod]
    public async Task NoConfiguredTeacherId_EmitsNoClaim_RegressionGuard()
    {
        var options = OptionsFor(configuredTeacherId: null);

        options.Get(TestAuthExtensions.TestAuthScheme).TeacherId.Should().Be(Guid.Empty);
        (await EmitsTeacherIdClaimAsync(options)).Should().BeFalse(
            "regression guard: the CI/dev default stays claim-less unless a test/dev host opts in");
    }

    [TestMethod]
    public async Task ConfiguredTeacherId_IsBoundAndClaimed_RegressionGuard()
    {
        var options = OptionsFor(DevTeacherId.ToString());

        options.Get(TestAuthExtensions.TestAuthScheme).TeacherId.Should().Be(DevTeacherId,
            "the binding lands on the options instance the TestAuth scheme handler is built with");
        (await EmitsTeacherIdClaimAsync(options)).Should().BeTrue(
            "regression guard: the claim leg already existed — the new part is that the value is configurable");
    }
}
