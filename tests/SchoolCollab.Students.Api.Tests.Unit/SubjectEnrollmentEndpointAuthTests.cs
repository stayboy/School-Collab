using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Students.Api.Tests.Unit;

/// <summary>
/// Acceptance criterion 16 — the three subject-enrolment routes
/// (subject-enrollment-requirement-flag.md §10) inherit the students group's
/// <c>RequireAuthIfOidc</c> policy. Host: a <see cref="WebApplicationFactory{TEntryPoint}"/>
/// over the real Students API with the production auth pipeline (OIDC ON), so an
/// unauthenticated caller is genuinely unauthenticated.
/// </summary>
/// <remarks>
/// No database or broker is reached: the authorization middleware rejects the request before
/// routing to a handler, so placeholder connection strings are enough for the host to boot —
/// and the challenge itself is the discriminating assertion, because only a mapped route that
/// inherits the group policy can produce it (an unmapped one 404s).
/// </remarks>
[TestClass]
public class SubjectEnrollmentEndpointAuthTests
{
    private static WebApplicationFactory<Program> NewFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("FeatureFlags:FEATURE:DisableOIDCAuth", "false");
            builder.UseSetting("ConnectionStrings:students-db",
                "Host=localhost;Database=students_placeholder;Username=test;Password=test");
            builder.UseSetting("ConnectionStrings:settings-db",
                "Host=localhost;Database=settings_placeholder;Username=test;Password=test");
            builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");
            builder.UseSetting("Outbox:ExchangeName", "students");
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        });

    private static void AssertChallenged(HttpResponseMessage response, string because) =>
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden }, because);

    [TestMethod]
    public async Task GetTopicEnrollment_WithoutACaller_IsChallenged()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/students/topic-enrollment?gradeLevelId={Guid.NewGuid()}&topicId={Guid.NewGuid()}");

        AssertChallenged(response, "the eligibility route inherits the students group's RequireAuthIfOidc");
    }

    [TestMethod]
    public async Task GetStudentSubjects_WithoutACaller_IsChallenged()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/students/{Guid.NewGuid()}/subjects");

        AssertChallenged(response, "the subjects union route inherits the students group's RequireAuthIfOidc");
    }

    [TestMethod]
    public async Task PutRequiresEnrollment_WithoutACaller_IsChallenged()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var response = await client.PutAsync(
            $"/students/topic-assignments/{Guid.NewGuid()}/requires-enrollment",
            new StringContent("{\"value\":true}", Encoding.UTF8, "application/json"));

        AssertChallenged(response, "the switch route inherits the students group's RequireAuthIfOidc");
    }
}
