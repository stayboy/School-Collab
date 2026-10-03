using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Assignments.Api.Auth;

/// <summary>
/// D5 (Q5, round <c>teacher-scope-auth</c>): the dev bypass's <c>teacher_id</c> claim, bound from
/// configuration.
///
/// <para>The claim emission already exists (<c>TestAuthHandler</c> omits the claim while
/// <see cref="TestAuthHandlerOptions.TeacherId"/> is <see cref="Guid.Empty"/>, which is the CI
/// default); what was missing was a configurable value, so the teacher-scoped read path could not
/// be exercised under the dev bypass at all. Set <c>TestAuth:TeacherId</c> to a real dev teacher
/// row id to opt in; an absent or empty value stays claim-less.</para>
///
/// <para>The scheme name is also the options instance name — the named-options pattern the auth
/// pipeline already uses for this handler, so the value lands on exactly the options instance
/// <c>TestAuthHandler</c> is built with.</para>
/// </summary>
public static class AssignmentDevTeacherIdentity
{
    /// <summary>Configuration key carrying the dev <c>teacher_id</c> claim value.</summary>
    public const string TeacherIdConfigKey = "TestAuth:TeacherId";

    /// <summary>Binds <see cref="TestAuthHandlerOptions.TeacherId"/> for the TestAuth scheme.</summary>
    public static IServiceCollection AddAssignmentDevTeacherIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<TestAuthHandlerOptions>(
            TestAuthExtensions.TestAuthScheme,
            options => options.TeacherId = configuration.GetValue(TeacherIdConfigKey, Guid.Empty));

        return services;
    }
}
