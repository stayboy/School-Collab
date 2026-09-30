using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Students.Api.Tests.Unit;

/// <summary>
/// AC9 — the new grade↔stream routes inherit the students group's
/// <c>RequireAuthorization</c>. Host: a <see cref="WebApplicationFactory{TEntryPoint}"/>
/// over the real Students API with the production auth pipeline (OIDC ON, i.e. the
/// default posture) so an UNAUTHENTICATED caller is genuinely unauthenticated — the
/// TestAuth-forcing integration factory cannot serve such a request.
/// </summary>
/// <remarks>
/// <para>No database or broker is reached: the authorization middleware rejects the
/// request before routing to the handler, so placeholder connection strings are
/// enough for the host to boot.</para>
/// <para>Pre-fix these routes do not exist; against the base commit the requests
/// would 404 (or, with auth off, reach a missing handler) rather than being
/// challenged — but the discriminating assertion here is the challenge itself, which
/// only a mapped route inheriting the group policy can produce.</para>
/// </remarks>
[TestClass]
public class GradeStreamEndpointAuthTests
{
    private static WebApplicationFactory<Program> NewFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Real auth pipeline: the OIDC bypass stays OFF so nothing auto-authenticates.
            builder.UseSetting("FeatureFlags:FEATURE:DisableOIDCAuth", "false");
            // Placeholders — never opened, but the hosts validate that they exist.
            builder.UseSetting("ConnectionStrings:students-db",
                "Host=localhost;Database=students_placeholder;Username=test;Password=test");
            builder.UseSetting("ConnectionStrings:settings-db",
                "Host=localhost;Database=settings_placeholder;Username=test;Password=test");
            builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");
            builder.UseSetting("Outbox:ExchangeName", "students");
            builder.UseEnvironment("Testing");

            // Keep the host hermetic: the outbox dispatcher's background RabbitMQ/DB
            // polling is irrelevant to an authorization challenge, and a failed
            // background service stops the test host (55s of connect retries per
            // request). The authorization middleware short-circuits before any
            // handler, so no other infrastructure is exercised either.
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        });

    [TestMethod]
    public async Task PostGradeLevelStream_WithoutACaller_IsChallenged()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/students/grade-levels/{Guid.NewGuid()}/streams",
            new StringContent("{\"streamCodedValueId\":\"" + Guid.NewGuid() + "\"}",
                System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden },
            "the assign route inherits the students group's RequireAuthorization");
    }

    [TestMethod]
    public async Task DeleteGradeLevelStream_WithoutACaller_IsChallenged()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var response = await client.DeleteAsync(
            $"/students/grade-levels/{Guid.NewGuid()}/streams/{Guid.NewGuid()}");

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden },
            "the unassign route inherits the students group's RequireAuthorization");
    }

    [TestMethod]
    public async Task GetGradeLevelStreams_WithoutACaller_IsChallenged()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/students/grade-levels/{Guid.NewGuid()}/streams");

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden },
            "the list route inherits the students group's RequireAuthorization");
    }
}
