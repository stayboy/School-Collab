using System.Net;
using System.Text;
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
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateProvisionalCodedValue;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Api.Tests.Unit;

/// <summary>
/// Pins the exact JSON body of the three CodedValue response payloads — <c>{"id":…}</c>,
/// <c>{"message":…}</c> and <c>{"parentId":…,"createdCount":…,"skippedCodes":[…]}</c> — and the two
/// <c>Created</c> <c>Location</c> values, while the handlers return <c>TypedResults</c> with unions
/// (spec <c>minimal-api-typed-responses</c> WP3).
/// <para>
/// The wire shape is the risk this file exists for: naming an anonymous payload as a record
/// (<c>IdResponse</c>, <c>MessageResponse</c>, <c>BulkCreateResponse</c>) is a compile-time change that must
/// leave the serialized bytes alone. A renamed or extra record property reddens here. Mirrors the
/// <c>NotificationTemplateRoutesTests</c> harness — only the command and provisional route groups are mapped
/// and every handler is a stub, so no database is reached and no auth policy is applied.
/// </para>
/// </summary>
[TestClass]
public class CodedValueRoutePayloadTests
{
    private const string BaseUrl = "/api/coded-values";

    private static readonly Guid CreatedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ParentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ── Stubs ──────────────────────────────────────────────────────────────

    private sealed class FixedCreateHandler(Guid id) : ICommandHandler<CreateCodedValue, Guid>
    {
        public Task<Guid> HandleAsync(CreateCodedValue command, CancellationToken ct = default)
            => Task.FromResult(id);
    }

    private sealed class FixedProvisionalCreateHandler(Guid id) : ICommandHandler<CreateProvisionalCodedValue, Guid>
    {
        public Task<Guid> HandleAsync(CreateProvisionalCodedValue command, CancellationToken ct = default)
            => Task.FromResult(id);
    }

    private sealed class CodeConflictCreateHandler(CodedValueCodeConflictException conflict)
        : ICommandHandler<CreateCodedValue, Guid>
    {
        public Task<Guid> HandleAsync(CreateCodedValue command, CancellationToken ct = default)
            => throw conflict;
    }

    private sealed class BulkHandler(BulkCreateResult? result, CodedValueNotFoundException? notFound)
        : ICommandHandler<BulkCreateCodedValues, BulkCreateResult>
    {
        public Task<BulkCreateResult> HandleAsync(BulkCreateCodedValues command, CancellationToken ct = default)
            => notFound is null ? Task.FromResult(result!) : throw notFound;
    }

    // ── Harness ────────────────────────────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        ICommandHandler<CreateCodedValue, Guid>? create = null,
        ICommandHandler<CreateProvisionalCodedValue, Guid>? createProvisional = null,
        ICommandHandler<BulkCreateCodedValues, BulkCreateResult>? bulk = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(create ?? new FixedCreateHandler(CreatedId));
        builder.Services.AddSingleton(createProvisional ?? new FixedProvisionalCreateHandler(CreatedId));
        builder.Services.AddSingleton(bulk
            ?? new BulkHandler(new BulkCreateResult(2, ["DUP"], ParentId), null));

        var app = builder.Build();
        app.MapGroup(BaseUrl)
            .MapCodedValueCommandRoutes()
            .MapCodedValueProvisionalRoutes();
        await app.StartAsync();

        return (app, ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient());
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private const string CreateBody =
        """{"code":"GRADE","name":"Grade","description":null,"parentId":null,"displayOrder":0}""";

    private const string ProvisionalCreateBody =
        """{"code":"GRADE","name":"Grade","description":null,"parentId":null,"displayOrder":0}""";

    private const string BulkBody =
        """{"parentId":"22222222-2222-2222-2222-222222222222","children":[{"code":"C1","name":"N1","description":null,"displayOrder":0}]}""";

    // ── Tests ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateCodedValue_201_WritesIdBodyAndCodedValueLocation()
    {
        var (app, client) = await StartAsync();
        await using (app)
        {
            var response = await client.PostAsync($"{BaseUrl}/", Json(CreateBody));

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            response.Headers.Location!.ToString().Should().Be(
                $"{BaseUrl}/{CreatedId}",
                "the Created location is part of the contract and must not change with the return type");
            (await response.Content.ReadAsStringAsync()).Should().Be(
                $$"""{"id":"{{CreatedId}}"}""",
                "the create body is exactly one id property — a record may not rename it or add members");
        }
    }

    [TestMethod]
    public async Task CreateProvisional_201_WritesIdBodyAndProvisionalLocation()
    {
        var (app, client) = await StartAsync();
        await using (app)
        {
            var response = await client.PostAsync($"{BaseUrl}/provisional", Json(ProvisionalCreateBody));

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            response.Headers.Location!.ToString().Should().Be($"{BaseUrl}/provisional/{CreatedId}");
            (await response.Content.ReadAsStringAsync()).Should().Be($$"""{"id":"{{CreatedId}}"}""");
        }
    }

    [TestMethod]
    public async Task CreateCodedValue_409_WritesMessageBodyOnly()
    {
        var conflict = new CodedValueCodeConflictException("GRADE", null, null, existingIsSharedBlueprint: true);
        var (app, client) = await StartAsync(create: new CodeConflictCreateHandler(conflict));
        await using (app)
        {
            var response = await client.PostAsync($"{BaseUrl}/", Json(CreateBody));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Be(
                $$"""{"message":"{{conflict.Message}}"}""",
                "the conflict body is exactly one message property carrying the exception message");
        }
    }

    [TestMethod]
    public async Task BulkCreate_200_WritesBulkCreateResponseBody()
    {
        var (app, client) = await StartAsync();
        await using (app)
        {
            var response = await client.PostAsync($"{BaseUrl}/bulk", Json(BulkBody));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be(
                $$"""{"parentId":"{{ParentId}}","createdCount":2,"skippedCodes":["DUP"]}""",
                "the bulk body keeps its three camelCase properties in declaration order");
        }
    }

    [TestMethod]
    public async Task BulkCreate_404_WritesMessageBodyOnly()
    {
        var notFound = new CodedValueNotFoundException(ParentId);
        var (app, client) = await StartAsync(
            bulk: new BulkHandler(null, notFound));
        await using (app)
        {
            var response = await client.PostAsync($"{BaseUrl}/bulk", Json(BulkBody));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().Be(
                $$"""{"message":"Parent coded value with ID '{{ParentId}}' not found."}""",
                "the missing-parent body is exactly one message property");
        }
    }
}
