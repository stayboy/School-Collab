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
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Settings.Api.Endpoints;
using SchoolCollab.Settings.Core.CQRS.AssignmentAiPrompts.Commands.UpsertTenantAssignmentAiPrompt;
using SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.CreateEntityCodeRule;
using SchoolCollab.Settings.Core.CQRS.FeatureFlags.Commands;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.SignatureConsentTexts.Commands.UpsertTenantSignatureConsentText;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Tests.Unit;

/// <summary>
/// Pins the exact JSON body of the payload shapes the typed-response conversion named, while the
/// handlers return <c>TypedResults</c> with unions (spec <c>minimal-api-typed-responses-settings</c>
/// WP4): the shared <c>MessageResponse</c> (<c>{"message":…}</c>), the distinct <c>ErrorResponse</c>
/// (<c>{"error":…}</c>) behind both <c>TypedResults.Json</c> 400 arms, and the two aggregate-specific
/// composites — <c>CreateFlagResponse</c> (<c>{"id":…}</c>) and <c>GeneratedCodeResponse</c>
/// (<c>{"code":…}</c>).
/// <para>
/// The wire shape is the risk this file exists for: naming an anonymous payload as a record is a
/// compile-time change that must leave the serialized bytes alone, and <c>error</c> must never be
/// normalised to <c>message</c>. Mirrors the <see cref="CodedValueRoutePayloadTests"/> harness — only
/// the route groups under test are mapped and every handler is a stub, so no database is reached and
/// no auth policy is applied.
/// </para>
/// </summary>
[TestClass]
public class SettingsResponsePayloadTests
{
    private const string ConfigUrl = "/api/config";
    private const string EntityCodeUrl = "/api/entity-code-rules";
    private const string SettingsUrl = "/api/settings";

    private static readonly Guid CreatedId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DeletedTemplateId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private const string RenameRejected = "Name is required.";
    private const string PromptRejected = "The system prompt exceeds the 4000-character limit.";
    private const string ConsentTextRejected = "The consent text exceeds the 4000-character limit.";
    private const string GeneratedCode = "TOPIC-0007";
    private const string MissingRuleCode = "MISSING";

    // ── Stubs ──────────────────────────────────────────────────────────────

    private sealed class FixedCreateFlagHandler(Guid id) : ICommandHandler<CreateFeatureFlag, Guid>
    {
        public Task<Guid> HandleAsync(CreateFeatureFlag command, CancellationToken ct = default)
            => Task.FromResult(id);
    }

    private sealed class RejectingRenameHandler(ArgumentException rejection) : ICommandHandler<RenameFeatureFlag>
    {
        public Task HandleAsync(RenameFeatureFlag command, CancellationToken ct = default)
            => throw rejection;
    }

    private sealed class FixedCreateRuleHandler(Guid id) : ICommandHandler<CreateEntityCodeRule, Guid>
    {
        public Task<Guid> HandleAsync(CreateEntityCodeRule command, CancellationToken ct = default)
            => Task.FromResult(id);
    }

    private sealed class CodeGenerator(string? code, EntityCodeRuleNotFoundException? notFound) : IEntityCodeGenerator
    {
        public Task<string> GenerateAsync(string ruleCode, CancellationToken cancellationToken = default)
            => GenerateWithNameAsync(ruleCode, null, cancellationToken);

        public Task<string> GenerateWithNameAsync(
            string ruleCode, string? nameHint, CancellationToken cancellationToken = default)
            => notFound is null ? Task.FromResult(code!) : throw notFound;
    }

    private sealed class RejectingAiPromptUpsertHandler(ArgumentException rejection)
        : ICommandHandler<UpsertTenantAssignmentAiPrompt, TenantAssignmentAiPromptDto>
    {
        public Task<TenantAssignmentAiPromptDto> HandleAsync(
            UpsertTenantAssignmentAiPrompt command, CancellationToken ct = default) => throw rejection;
    }

    private sealed class RejectingConsentTextUpsertHandler(ArgumentException rejection)
        : ICommandHandler<UpsertTenantSignatureConsentText, TenantSignatureConsentTextDto>
    {
        public Task<TenantSignatureConsentTextDto> HandleAsync(
            UpsertTenantSignatureConsentText command, CancellationToken ct = default) => throw rejection;
    }

    /// <summary>Never reached by the reason-guard test; [FromServices] resolves before the guard runs.</summary>
    private sealed class UnreachedDeleteTemplateHandler : ICommandHandler<DeleteTenantNotificationTemplate>
    {
        public Task HandleAsync(DeleteTenantNotificationTemplate command, CancellationToken ct = default)
            => throw new InvalidOperationException("the reason guard must return before the handler runs");
    }

    // ── Harness ────────────────────────────────────────────────────────────

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(
        ICommandHandler<CreateFeatureFlag, Guid>? createFlag = null,
        ICommandHandler<RenameFeatureFlag>? renameFlag = null,
        ICommandHandler<CreateEntityCodeRule, Guid>? createRule = null,
        IEntityCodeGenerator? generator = null,
        ICommandHandler<UpsertTenantAssignmentAiPrompt, TenantAssignmentAiPromptDto>? aiPrompt = null,
        ICommandHandler<UpsertTenantSignatureConsentText, TenantSignatureConsentTextDto>? consentText = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(createFlag ?? new FixedCreateFlagHandler(CreatedId));
        builder.Services.AddSingleton(renameFlag ?? new RejectingRenameHandler(new ArgumentException(RenameRejected)));
        builder.Services.AddSingleton(createRule ?? new FixedCreateRuleHandler(CreatedId));
        builder.Services.AddSingleton(generator ?? new CodeGenerator(GeneratedCode, null));
        builder.Services.AddSingleton(aiPrompt
            ?? new RejectingAiPromptUpsertHandler(new ArgumentException(PromptRejected)));
        builder.Services.AddSingleton(consentText
            ?? new RejectingConsentTextUpsertHandler(new ArgumentException(ConsentTextRejected)));
        builder.Services.AddSingleton<ICommandHandler<DeleteTenantNotificationTemplate>>(
            new UnreachedDeleteTemplateHandler());

        var app = builder.Build();
        app.MapGroup(ConfigUrl).MapConfigFlagRoutes();
        app.MapGroup(EntityCodeUrl).MapEntityCodeRuleRoutes();
        app.MapGroup(SettingsUrl)
            .MapAssignmentAiPromptRoutes()
            .MapSignatureConsentTextRoutes()
            .MapNotificationTemplateRoutes();
        await app.StartAsync();

        return (app, ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient());
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private const string CreateFlagBody =
        """{"key":"my-flag","name":"My flag","description":null,"isEnabled":true,"reason":"new feature"}""";

    private const string RenameFlagBody = """{"name":"Renamed","description":null,"reason":"tidy-up"}""";

    private const string CreateRuleBody =
        """{"code":"TOPIC","name":"Topic","description":null,"isActive":true,"segments":[]}""";

    private const string GenerateBody = """{"ruleCode":"MISSING","nameHint":null}""";

    private const string AiPromptBody = """{"systemPrompt":"Be terse.","isLocked":false}""";

    private const string ConsentTextBody = """{"consentText":"I agree."}""";

    // ── Tests ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateFlag_201_WritesCreateFlagResponseBodyAndFlagLocation()
    {
        var (app, client) = await StartAsync();
        await using (app)
        {
            var response = await client.PostAsync($"{ConfigUrl}/flags", Json(CreateFlagBody));

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            response.Headers.Location!.ToString().Should().Be(
                $"{ConfigUrl}/flags/my-flag",
                "the Created location is part of the contract and must not change with the return type");
            (await response.Content.ReadAsStringAsync()).Should().Be(
                $$"""{"id":"{{CreatedId}}"}""",
                "CreateFlagResponse is exactly one camelCase id property — a record may not rename it or add members");
        }
    }

    [TestMethod]
    public async Task RenameFlag_400_WritesMessageResponseBodyOnly()
    {
        var (app, client) = await StartAsync();
        await using (app)
        {
            var response = await client.PutAsync($"{ConfigUrl}/flags/my-flag", Json(RenameFlagBody));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Be(
                $$"""{"message":"{{RenameRejected}}"}""",
                "the 400 body is exactly one message property carrying the exception message");
        }
    }

    [TestMethod]
    public async Task CreateEntityCodeRule_201_WritesTheSharedIdResponseBodyAndLocation()
    {
        var (app, client) = await StartAsync();
        await using (app)
        {
            var response = await client.PostAsync($"{EntityCodeUrl}/", Json(CreateRuleBody));

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            response.Headers.Location!.ToString().Should().Be($"{EntityCodeUrl}/{CreatedId}");
            (await response.Content.ReadAsStringAsync()).Should().Be(
                $$"""{"id":"{{CreatedId}}"}""",
                "the rule create reuses the shared IdResponse, which keeps the anonymous {\"id\":…} bytes");
        }
    }

    [TestMethod]
    public async Task GenerateCode_200_WritesGeneratedCodeResponseBodyOnly()
    {
        var (app, client) = await StartAsync();
        await using (app)
        {
            var response = await client.PostAsync($"{EntityCodeUrl}/generate", Json(GenerateBody));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be(
                $$"""{"code":"{{GeneratedCode}}"}""",
                "GeneratedCodeResponse is exactly one camelCase code property — the anonymous {\"code\":…} shape");
        }
    }

    [TestMethod]
    public async Task GenerateCode_404_WritesMessageResponseBodyOnly()
    {
        var notFound = new EntityCodeRuleNotFoundException(Guid.NewGuid());
        var (app, client) = await StartAsync(generator: new CodeGenerator(null, notFound));
        await using (app)
        {
            var response = await client.PostAsync($"{EntityCodeUrl}/generate", Json(GenerateBody));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().Be(
                $$"""{"message":"No active entity-code rule '{{MissingRuleCode}}'."}""",
                "a 404 arm that carried a message before the conversion must still carry exactly that message");
        }
    }

    [TestMethod]
    public async Task UpsertAssignmentAiPrompt_400_WritesErrorResponseBodyOnly()
    {
        var (app, client) = await StartAsync();
        await using (app)
        {
            var response = await client.PutAsync($"{SettingsUrl}/assignment-ai-prompt", Json(AiPromptBody));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Be(
                $$"""{"error":"{{PromptRejected}}"}""",
                "this route has always serialized the message under an error key — TypedResults.Json with "
                + "ErrorResponse must not normalise it to message");
        }
    }

    [TestMethod]
    public async Task UpsertSignatureConsentText_400_WritesErrorResponseBodyOnly()
    {
        var (app, client) = await StartAsync();
        await using (app)
        {
            var response = await client.PutAsync($"{SettingsUrl}/signature-consent-text", Json(ConsentTextBody));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Be(
                $$"""{"error":"{{ConsentTextRejected}}"}""",
                "the second TypedResults.Json 400 site keeps the same error key and body shape");
        }
    }

    /// <summary>
    /// Round b2c re-pinned this on the by-id DELETE: the audited tombstone is addressed by template
    /// id, and the reason guard still runs before the handler and still writes a MessageResponse.
    /// </summary>
    [TestMethod]
    public async Task ResetNotificationTemplate_400_WritesTheRequiredReasonMessage()
    {
        var (app, client) = await StartAsync();
        await using (app)
        {
            var response = await client.DeleteAsync(
                $"{SettingsUrl}/notification-templates/{DeletedTemplateId}?reason=%20");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Be(
                """{"message":"A reason is required to reset a notification template."}""",
                "the reason guard writes a MessageResponse — its Message property used to be spelled with a "
                + "capital M in an anonymous object, and camelCase serialization must still produce lowercase");
        }
    }
}
