using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Diagnostic repro for the live failure reported during create-student-enrollment:
/// <c>POST https+http://students-api/students/enrollments</c> failing inside
/// <see cref="TenantPropagationDelegatingHandler.SendAsync"/>.
///
/// <para><b>What this pins:</b> when Aspire service discovery has NO endpoint
/// metadata for <c>students-api</c> (the API project never started / crashed at
/// startup, or the Admin host was launched without the AppHost), the request URI
/// keeps its UNRESOLVED <c>https+http</c> scheme and the failure surfaces from the
/// typed-client pipeline — with the raw <c>https+http://students-api/...</c> URL in
/// the message. Seeing that scheme in an error is therefore a SERVICE-DISCOVERY
/// resolution symptom, not a tenant-handler bug: the handler is stateless and its
/// cache-read failures are fault-isolated (proceed without header).</para>
///
/// <para>The healthy-path twin test proves the SAME pipeline succeeds once
/// configuration-based endpoint metadata exists.</para>
/// </summary>
[TestClass]
public class TenantPropagationEnrollPipelineDiagnosticTests
{
    private sealed class StubDevTenantSelection : IDevTenantSelection
    {
        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default) =>
            Task.FromResult<Guid?>(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Mirrors AddServiceDefaults' ConfigureHttpClientDefaults + the
    /// AddStudentsModule typed-client wiring verbatim.</summary>
    private static ServiceProvider BuildPipeline(Dictionary<string, string?>? configOverrides)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configOverrides ?? new()).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IDevTenantSelection>(new StubDevTenantSelection());

        // = AddServiceDefaults()
        services.AddLogging();
        services.AddServiceDiscovery();
        services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        // = AddStudentsModule() (the enroll path)
        services.TryAddTransient<TenantPropagationDelegatingHandler>();
        services.AddHttpClient<StudentsApiClient>(client =>
                client.BaseAddress = new Uri("https+http://students-api"))
            .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();

        // StudentsApiClient's ctor also wants the shared coded-values client —
        // a detached instance suffices here (enrichment is not under test).
        ServicesHelper.RegisterDetachedCodedValuesClient(services);

        return services.BuildServiceProvider();
    }

    [TestMethod]
    public async Task UnresolvedServiceDiscovery_FailsAsNoSuchHost_FromInsideSendAsync()
    {
        // NO endpoint metadata for students-api anywhere → exactly the state
        // when the students-api resource never published an endpoint (API not
        // started / crashed / Admin launched without the AppHost). The resolver
        // passes the literal hostname through, DNS fails, and the error
        // surfaces from TenantPropagationDelegatingHandler.base.SendAsync.
        await using var sp = BuildPipeline(configOverrides: null);
        var client = sp.GetRequiredService<StudentsApiClient>();

        var act = async () => await client.EnrollStudentAsync(new(
            StudentId: Guid.NewGuid(),
            PeriodId: Guid.NewGuid(),
            GradeCodedValueId: Guid.NewGuid(),
            StreamCodedValueId: null,
            EnrolledOn: null));

        var ex = (await act.Should().ThrowAsync<HttpRequestException>()).Which;
        var text = ex.ToString();
        text.Should().Contain("No such host",
            "an unresolvable service degrades to a literal-DNS lookup of 'students-api' — " +
            "this signature means SERVICE DISCOVERY had no endpoint for the API resource " +
            "(resource never started / Admin launched without the AppHost), NOT a " +
            "TenantPropagationDelegatingHandler defect");
        text.Should().Contain("TenantPropagationDelegatingHandler.SendAsync")
            .And.Contain("ResolvingHttpDelegatingHandler.SendAsync",
                "the resolver sat directly beneath the tenant handler — the failure bubbled " +
                "through base.SendAsync, which is why stack traces blame the handler");
        text.Should().NotContain("https+http",
            "the scheme is consumed before the wire; only the bare hostname reaches DNS");
    }

    [TestMethod]
    public async Task ResolvedViaConfiguration_SamePipeline_ReachesTheWire()
    {
        // Configuration-based endpoint metadata — the same mechanism DCP-driven
        // discovery replaces with live ports. Points at a loopback port where
        // nothing listens: connection REFUSED (not name-resolution) proves the
        // pipeline resolved the address and physically attempted the call.
        await using var sp = BuildPipeline(new Dictionary<string, string?>
        {
            ["services:students-api:http"] = "127.0.0.1:59999",
            ["services:students-api:https"] = "127.0.0.1:59999",
        });
        var client = sp.GetRequiredService<StudentsApiClient>();

        var act = async () => await client.EnrollStudentAsync(new(
            StudentId: Guid.NewGuid(),
            PeriodId: Guid.NewGuid(),
            GradeCodedValueId: Guid.NewGuid(),
            StreamCodedValueId: null,
            EnrolledOn: null));

        // Standard resilience retries amplify the attempts, but the surfaced
        // exception must be a network-layer failure against the resolved
        // loopback endpoint — not an unresolved-scheme error.
        var ex = (await act.Should().ThrowAsync<Exception>()).Which;
        ex.ToString().Should().NotContain("https+http",
            "with endpoint metadata present, the https+http scheme is rewritten before hitting the wire");
        ex.ToString().Should().ContainAny("refused", "connection", "socket", "No connection",
            "the failure is now a physical connect failure against the RESOLVED endpoint");
    }
}

/// <summary>Small helper so the diagnostic stays focused: registers a detached
/// <see cref="CodedValuesApiClient"/> (its own HttpClient, no pipeline) purely to
/// satisfy <see cref="StudentsApiClient"/>'s constructor.</summary>
internal static class ServicesHelper
{
    public static void RegisterDetachedCodedValuesClient(IServiceCollection services)
    {
        services.AddSingleton(new CodedValuesApiClient(
            new HttpClient { BaseAddress = new Uri("http://localhost:1") }));
    }
}
