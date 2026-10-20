using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Api;
using SchoolCollab.Students.Api;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// OpenAPI surface guard (spec <c>minimal-api-composition</c> §WP7, decision D7). Every API host maps
/// <c>AddOpenApi()</c>/<c>MapOpenApi()</c>, yet nothing forced a mapped route to appear in the generated
/// document. This test reads the real document out of an in-process host and, per host, asserts:
/// <list type="number">
///   <item>every path the host maps is present in the document at <c>/openapi/v1.json</c> — a
///        mapped-but-undocumented route fails here;</item>
///   <item>every operation documents a non-empty <c>responses</c> object — an operation with no
///        response at all fails here;</item>
///   <item>how many operations document a response schema (<c>content</c> or <c>$ref</c>) — pinned at the
///        measured baseline, see <see cref="SchemaPin"/>.</item>
/// </list>
/// <para>
/// Point 3 is a <b>debt ledger, not a quality gate</b>: it records today's absence of response schemas so
/// the number cannot drift unnoticed. The gate is points 1 and 2.
/// </para>
/// <para>
/// Scope guard: the assertion is over the <b>document</b>, never over handler behaviour — it replaces no
/// route test, it only makes a missing one visible. The host is the
/// <c>AssignmentDraftRoutesTests.StartHostAsync</c> precedent: <c>Program.cs</c> is never started, so no
/// Postgres, RabbitMQ or Redis is needed; only the host's own <c>Map&lt;Domain&gt;Endpoints</c>
/// orchestrator is mapped.
/// </para>
/// </summary>
[TestClass]
public class OpenApiSurfaceArchitectureTests
{
    private const string DocumentPath = "/openapi/v1.json";

    /// <summary>
    /// The measured baseline each host pins: how many of its operations document a response schema
    /// (<c>content</c> or <c>$ref</c>). Settings.Api measures <b>52 of 68</b> after both typed-response
    /// rounds (spec <c>minimal-api-typed-responses</c> pilot + <c>-settings</c> follow-up), the
    /// notification-templates-b2c by-id route swap (measured 2026-10-19 from the generated document) and
    /// the round-notification-templates-b3a applicability read (measured 2026-10-20);
    /// Students.Api measures <b>0 of 146</b>, untouched by any of them. Raise the number in the same PR
    /// that converts handlers — a conversion that leaves it stale means the pin no longer describes the
    /// document.
    /// </summary>
    private const int SettingsSchemaPin = 52;

    private const int StudentsSchemaPin = 0;

    /// <summary>Strips an ASP.NET route constraint (<c>{id:guid}</c>) into the OpenAPI template (<c>{id}</c>).</summary>
    private static readonly Regex RouteConstraint = new(@"\{([^}:]+):[^}]*\}", RegexOptions.Compiled);

    [TestMethod]
    public async Task SettingsApi_DocumentCoversEveryMappedOperation()
        => await AssertDocumentCoversEveryMappedOperationAsync(
            "Settings.Api",
            app =>
            {
                var flags = Flags(app);
                // The same nine orchestrators Program.cs maps, in the same order.
                app.MapCodedValueEndpoints(flags);
                app.MapTenantEndpoints(flags);
                app.MapConfigEndpoints(flags);
                app.MapEntityCodeRuleEndpoints(flags);
                app.MapNotificationPolicyEndpoints(flags);
                app.MapNotificationTemplateEndpoints(flags);
                app.MapAssignmentPolicyEndpoints(flags);
                app.MapSignatureConsentTextEndpoints(flags);
                app.MapAssignmentAiPromptEndpoints(flags);
            },
            SettingsSchemaPin);

    [TestMethod]
    public async Task StudentsApi_DocumentCoversEveryMappedOperation()
        => await AssertDocumentCoversEveryMappedOperationAsync(
            "Students.Api",
            app => app.MapStudentEndpoints(Flags(app)),
            StudentsSchemaPin);

    /// <summary>
    /// Assignments.Api cannot produce an OpenAPI document, so this test pins the defect instead of
    /// asserting a document that does not exist. Generating it throws while building one of the host's
    /// request-body schemas — <c>JsonSchemaExporter.GetJsonSchemaAsNode</c> →
    /// <c>OpenApiSchemaService.CreateSchema</c> → <c>OpenApiDocumentService.GetJsonRequestBody</c>, which
    /// raises <see cref="JsonException"/> "The JSON value could not be converted to System.Guid". The defect
    /// is pre-existing and unrelated to this test's host: Settings.Api and Students.Api generate their
    /// documents from the identical harness. Assignments.Api's published OpenAPI document is therefore
    /// unavailable, and fixing it means changing a request DTO's serialization — a wire shape, which this
    /// round's scope excludes.
    /// <para>
    /// When the defect is fixed this test starts failing on the null-exception assertion: delete it (do not
    /// adjust it) and move Assignments.Api onto
    /// <see cref="AssertDocumentCoversEveryMappedOperationAsync"/>.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task AssignmentsApi_DocumentGeneration_FailsOnTheKnownSchemaExporterDefect()
    {
        await using var app = await StartHostAsync(app => app.MapAssignmentEndpoints(Flags(app)));
        var client = CreateClient(app);

        Exception? thrown = null;
        HttpStatusCode? statusCode = null;
        try
        {
            using var response = await client.GetAsync(DocumentPath);
            statusCode = response.StatusCode;
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        // The host throws; a host that instead turns the failure into a non-200 status is equally a
        // failure to publish the document. Only a 200 — the defect being gone — is a regression here.
        statusCode.Should().NotBe(HttpStatusCode.OK,
            "Assignments.Api's OpenAPI document is expected to be UNAVAILABLE: the document endpoint must "
            + "not start returning 200 without this pin being deleted and the full document assertions "
            + "moving onto AssertDocumentCoversEveryMappedOperationAsync");

        thrown.Should().BeOfType<JsonException>(
            "Assignments.Api's document generation throws the known System.Text.Json schema-exporter "
            + $"defect (observed at {DocumentPath}); if it now succeeds the defect is fixed — delete this "
            + "test rather than adjusting it, and assert the document instead");
        thrown!.Message.Should().Contain("System.Guid",
            "the pinned defect is the schema exporter failing to convert a System.Guid request-body "
            + "property (JsonSchemaExporter.GetJsonSchemaAsNode → OpenApiDocumentService.GetJsonRequestBody) "
            + "— a different failure means this pin no longer describes what it records");
    }

    private static async Task AssertDocumentCoversEveryMappedOperationAsync(
        string hostName, Action<WebApplication> mapEndpoints, int schemaPin)
    {
        await using var app = await StartHostAsync(mapEndpoints);

        var mappedPaths = MappedRoutePaths(app);
        mappedPaths.Should().NotBeEmpty(
            $"{hostName} must map at least one route — an empty host would make every assertion below vacuous");

        using var document = await GetOpenApiDocumentAsync(app, hostName);
        var paths = document.RootElement.GetProperty("paths");

        paths.EnumerateObject().Should().NotBeEmpty(
            $"{hostName} maps AddOpenApi(), so {DocumentPath} must document the paths it serves");

        // Composition observation: a route mapped at a group root as "group.MapGet(\"/\", …)" reaches the
        // route table as "/contacts/" (prefix + "/"), while the generated document lists it without the
        // trailing slash. Both spellings are accepted rather than the test blessing either one.
        var documentedPaths = paths.EnumerateObject()
            .SelectMany(path => new[] { path.Name, NormalizePath(path.Name) })
            .ToHashSet(StringComparer.Ordinal);

        foreach (var mappedPath in mappedPaths)
        {
            documentedPaths.Should().Contain(mappedPath,
                $"{hostName} maps {mappedPath} but the OpenAPI document at {DocumentPath} does not include it");
        }

        var operationsChecked = 0;
        var operationsWithSchema = new List<string>();
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                var location = $"{hostName} {operation.Name.ToUpperInvariant()} {path.Name}";

                operation.Value.TryGetProperty("responses", out var responses).Should().BeTrue(
                    $"{location} must document at least one response — an undocumented operation cannot be "
                    + "consumed from the generated client");
                responses.EnumerateObject().Should().NotBeEmpty(
                    $"{location} must document a non-empty responses object");

                operationsChecked++;

                var documentsSchema = responses.EnumerateObject().Any(response =>
                    (response.Value.TryGetProperty("content", out var content) && content.EnumerateObject().Any())
                    || response.Value.TryGetProperty("$ref", out _));

                if (documentsSchema)
                {
                    operationsWithSchema.Add(location);
                }
            }
        }

        operationsChecked.Should().BeGreaterThan(0,
            $"{hostName}'s document must contain at least one operation — otherwise the loop above asserted nothing");

        // Debt ledger (spec D2/D7). Raise the number in the same PR that converts handlers to TypedResults
        // or chains .Produces<T>() — a conversion that leaves it stale means the pin is lying.
        operationsWithSchema.Count.Should().Be(schemaPin,
            $"{hostName} documents a response schema for {operationsWithSchema.Count} of {operationsChecked} "
            + $"operations, but the pin records {schemaPin}. Raise this number deliberately in the same PR "
            + "that converts handlers, because a conversion that leaves it stale means the pin no longer "
            + "describes the document. Raise it to the MEASURED count, never to a predicted one. Settings.Api "
            + "(67 operations, measured 2026-10-19 after round notification-templates-b2c): 24 CodedValue-pilot "
            + "operations (converted in PR #364, 19 of them documented a schema then), 29 operations whose "
            + "200/201 body documents a schema, 5 whose only body is a 400/409 message and 9 with no body at "
            + "all — 24 + 29 + 5 + 9 = 67. Round b2c swapped the two (kind, channel)-keyed template routes for "
            + "three by-id ones (POST notification-templates, PUT notification-templates/{id}, DELETE "
            + "notification-templates/{id}), which is −2 +3 operations: one body route left and two arrived, "
            + "one message-only route left and one arrived. Of the 5 message-only operations 3 document the 400 "
            + "content (PUT config/flags/{key}, PUT entity-code-rules/{id}/overrides, DELETE notification-templates/"
            + "{id}) and 2 are the four-arm handlers kept on IResult (PUT entity-code-rules/{id}, "
            + "PUT tenants/me/default), which document no schema at all — a declared IResult return type loses "
            + "the arm types, exactly as the pilot's four-arm approve did. So 19 + 29 + 3 = 51 of them document "
            + "a schema, and the 67 - 51 = 16 remaining operations document none: 5 from the pilot (recover, disable, "
            + "enable, DELETE override, the 4-arm approve), the 9 genuinely body-less routes and the two 4-arm "
            + "handlers kept on IResult, which cannot carry response types. Round b3a added one operation — GET "
            + "notification-templates/applicable/{gradeLevelId}, typed Ok<ApplicableNotificationTemplatesDto> — so "
            + "it documents a schema: 51 + 1 = 52 of 68. Students.Api was not "
            + "converted and stays at 0 of 146"
            + (operationsWithSchema.Count > 0
                ? $". First operations now documenting a schema: {string.Join("; ", operationsWithSchema.Take(3))}"
                : string.Empty));
    }

    /// <summary>A minimal host that maps only the host's own endpoints orchestrator.</summary>
    private static async Task<WebApplication> StartHostAsync(Action<WebApplication> mapEndpoints)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Critical);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());
        builder.Services.AddOpenApi();

        var app = builder.Build();
        app.MapOpenApi();
        mapEndpoints(app);
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
        => ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

    private static IFeatureFlagService Flags(WebApplication app)
        => app.Services.GetRequiredService<IFeatureFlagService>();

    /// <summary>
    /// Every path the host actually mapped, in OpenAPI template form (<c>/assignments/{id:guid}</c> →
    /// <c>/assignments/{id}</c>), so a mapped-but-undocumented route fails rather than passes. The host's
    /// own document endpoint is excluded — it documents itself, not a domain route.
    /// </summary>
    private static IReadOnlyList<string> MappedRoutePaths(WebApplication app)
        => [.. ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(rawText => !string.IsNullOrEmpty(rawText))
            .Select(rawText => NormalizePath("/" + RouteConstraint.Replace(rawText!, "{$1}").TrimStart('/')))
            .Where(path => !path.StartsWith("/openapi/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)];

    private static string NormalizePath(string path)
        => path.Length > 1 ? path.TrimEnd('/') : path;

    private static async Task<JsonDocument> GetOpenApiDocumentAsync(WebApplication app, string hostName)
    {
        using var response = await CreateClient(app).GetAsync(DocumentPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"{hostName} must serve the generated document at {DocumentPath}");

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Real-auth posture with the dark-launch flags off, so every group's policy is applied.</summary>
    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => false;

        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();

        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default)
            => Task.FromResult(IsEnabled(featureKey));
    }
}
