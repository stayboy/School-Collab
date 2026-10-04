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
///
/// <para>Round <c>dev-teacher-identity-wiring</c> adds the <b>blank-value</b> cases: the AppHost
/// fans its fail-closed <c>dev-teacher-id</c> base default in as <c>TestAuth__TeacherId=""</c>,
/// and a typed <c>GetValue&lt;Guid&gt;(key, Guid.Empty)</c> fallback does NOT cover a present empty
/// string — it converts, and throws. Absent, empty and whitespace must all resolve to
/// <see cref="Guid.Empty"/> (claim-less, no crash), and a real Guid must still bind.</para>
/// </summary>
[TestClass]
public class AssignmentDevTeacherIdentityTests
{
    private static readonly Guid DevTeacherId = Guid.Parse("00000000-0000-0000-0000-0000000000d7");

    /// <summary>Builds the options instance the TestAuth scheme handler is built with, from the raw
    /// configuration map — no entries means the key is absent, a <c>""</c> value means present but
    /// blank (the two are NOT the same to a typed <c>GetValue</c> call).</summary>
    private static IOptionsMonitor<TestAuthHandlerOptions> OptionsFor(
        params (string Key, string? Value)[] configurationEntries)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationEntries.ToDictionary(e => e.Key, e => e.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddAssignmentDevTeacherIdentity(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<TestAuthHandlerOptions>>();
    }

    /// <summary>The configured-path shorthand used by the tests below.</summary>
    private static IOptionsMonitor<TestAuthHandlerOptions> OptionsForTeacherId(string? value)
        => OptionsFor((AssignmentDevTeacherIdentity.TeacherIdConfigKey, value));

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
    public async Task AbsentTeacherId_EmitsNoClaim_RegressionGuard()
    {
        var options = OptionsFor();

        options.Get(TestAuthExtensions.TestAuthScheme).TeacherId.Should().Be(Guid.Empty);
        (await EmitsTeacherIdClaimAsync(options)).Should().BeFalse(
            "regression guard: the CI/dev default stays claim-less unless a test/dev host opts in");
    }

    [TestMethod]
    public async Task EmptyTeacherId_IsClaimLess_AndDoesNotThrow()
    {
        // THE P1 (round `dev-teacher-identity-wiring`): the AppHost's fail-closed base default
        // reaches the API as `TestAuth__TeacherId=""`. `GetValue<Guid>(key, Guid.Empty)` does not
        // cover that — it converts the present empty string and throws
        // "Failed to convert configuration value '' at 'TestAuth:TeacherId' to type 'System.Guid'",
        // so the fail-closed default crashed the host instead of staying claim-less.
        var options = OptionsForTeacherId(string.Empty);

        options.Get(TestAuthExtensions.TestAuthScheme).TeacherId.Should().Be(Guid.Empty,
            "a present but empty value must resolve to no dev identity, exactly like an absent one");
        (await EmitsTeacherIdClaimAsync(options)).Should().BeFalse(
            "an empty value names no teacher, so the dev bypass must stay claim-less");
    }

    [TestMethod]
    public async Task WhitespaceTeacherId_IsClaimLess_AndDoesNotThrow()
    {
        var options = OptionsForTeacherId("   ");

        options.Get(TestAuthExtensions.TestAuthScheme).TeacherId.Should().Be(Guid.Empty,
            "whitespace is a blank value, not a Guid — the same fail-closed posture as empty");
        (await EmitsTeacherIdClaimAsync(options)).Should().BeFalse(
            "a blank value names no teacher, so the dev bypass must stay claim-less");
    }

    [TestMethod]
    public async Task ConfiguredTeacherId_IsBoundAndClaimed_RegressionGuard()
    {
        var options = OptionsForTeacherId(DevTeacherId.ToString());

        options.Get(TestAuthExtensions.TestAuthScheme).TeacherId.Should().Be(DevTeacherId,
            "the binding lands on the options instance the TestAuth scheme handler is built with");
        (await EmitsTeacherIdClaimAsync(options)).Should().BeTrue(
            "regression guard: the claim leg already existed — the new part is that the value is configurable");
    }
}
