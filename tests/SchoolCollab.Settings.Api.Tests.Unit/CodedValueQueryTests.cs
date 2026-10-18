using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Api.Endpoints;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.SearchCodedValues;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Tests.Unit;

/// <summary>
/// Pins the HTTP contract of the CodedValue <em>query</em> routes that the handler tests cannot see:
/// the route short-circuits a blank search before it touches the query handler
/// (<c>CodedValueQueryRoutes</c>), so the empty-array answer is a property of the endpoint, not of
/// <c>SearchCodedValuesHandler</c>. Pruning round 2026-10-17 moved the contract assertion here and left
/// one representative guard test on the handler (Q24).
/// <para>
/// The measured contract has two halves, and the second one was only discovered while writing this test:
/// <b>blank</b> text (<c>?text=</c>, <c>?text=%20%20</c>) is a 200 with an empty array, while a
/// <b>missing</b> <c>text</c> parameter is a 400 — the parameter is required by the minimal-API signature,
/// so the handler is never reached on that path either.
/// </para>
/// <para>
/// The stub throws if it is ever called: that is the discriminating half — a route that lost its guard
/// would still return 200, but it would route the blank text through the handler and fail this test.
/// </para>
/// </summary>
[TestClass]
public class CodedValueQueryTests
{
    private sealed class SearchHandlerThatMustNotBeCalled : IQueryHandler<SearchCodedValues, CodedValueDto[]>
    {
        public Task<CodedValueDto[]> HandleAsync(SearchCodedValues query, CancellationToken ct = default)
            => throw new InvalidOperationException(
                "the route must short-circuit a blank search text before calling the query handler");
    }

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IQueryHandler<SearchCodedValues, CodedValueDto[]>>(
            new SearchHandlerThatMustNotBeCalled());

        var app = builder.Build();
        app.MapGroup("/api/coded-values").MapCodedValueQueryRoutes();
        await app.StartAsync();

        return (app, ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient());
    }

    [TestMethod]
    public async Task Search_WithBlankText_Returns200EmptyArrayWithoutCallingTheHandler()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        foreach (var query in new[] { "?text=", "?text=%20%20", "?text=%20" })
        {
            var response = await client.GetAsync($"/api/coded-values/search{query}");

            response.StatusCode.Should().Be(HttpStatusCode.OK, $"blank search '{query}' returns an empty page, not an error");
            var body = await response.Content.ReadAsStringAsync();
            JsonDocument.Parse(body).RootElement.GetArrayLength().Should().Be(0,
                $"blank search '{query}' must return an empty array");
        }
    }

    [TestMethod]
    public async Task Search_WithoutTheRequiredTextParameter_Returns400()
    {
        // Measured while writing the test above: the parameter is non-nullable in the route signature,
        // so a request that omits it is rejected by model binding before the short-circuit runs.
        // Pinned so a change to `string? text` (which would silently turn 400 into 200 []) is deliberate.
        var (app, client) = await StartAsync();
        await using var _ = app;

        var response = await client.GetAsync("/api/coded-values/search");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a search request with no text parameter is rejected by binding, not answered with an empty array");
    }
}
