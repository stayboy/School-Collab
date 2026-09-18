using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;
using Serilog;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Api.Endpoints;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core;
using SchoolCollab.Settings.Core;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Core.Features;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRabbitMQClient("rabbitmq");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<AssignmentTypeDto>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<AssignmentStatusDto>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<GradingFormatDto>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<TargetAudienceTypeDto>());
    // AI spec §3.2: question payloads round-trip the discriminator as a string
    // (e.g. "multipleChoice") — register the converter on the same options block
    // as the other assignment enums so existing callers stay valid.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<QuestionTypeDto>());
    // WS-A1 / FR-210-212: content module + AI-generation resource enums
    // round-trip as strings to keep the wizard's payload self-describing.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ModuleTypeDto>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ResourceKindDto>());
    // WS-A2 / spec §7 Q2: approval status is nullable on the wire — the
    // converter is registered on the same options block so the field
    // round-trips as the string name (Pending / Approved / Rejected)
    // and null stays null.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ApprovalStatusDto>());
    // WS-C1/WS-F3 (ar-15-signoff-relocation): the guardian sign-off enums must bind as
    // string names in the REAL host — the Families guardian POST body round-trips
    // SignatureTypeDto as "Typed"/"Click" and the context/certificate GETs serialize
    // SignOffStateDto as names. Previously these converters were registered ONLY in the
    // two guardian test files' self-registered JsonOptions, so the live host failed the
    // guardian POST body binding with 400 (and the teacher-POST seam carried them only
    // client-side). They now belong here, on the same host options block as the other
    // assignment enums, so both the guardian and pre-existing teacher sign surfaces bind.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<SignatureTypeDto>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<SignOffStateDto>());
    // WS-E2b / ar-18: the two delivery enums on the failures read side
    // (NotificationFailureDto.Channel / .Kind) were the only pair of the assignment
    // enums missing here, so the real host emitted them as NUMBERS while the client's
    // read-side converter masked it. Same lesson as the ar-15 block above: a converter
    // registered only in a test host hides the live gap.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<NotificationKindDto>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ContactChannelDto>());
});

var cacheConnectionString = builder.Configuration.GetConnectionString("cache")
    ?? builder.Configuration["Aspire:StackExchange:Redis:ConnectionString"];

if (string.IsNullOrWhiteSpace(cacheConnectionString))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.AddRedisDistributedCache("cache");
}

// WS-E1 (ar-14-deep-links): DataProtection keyring shared with the Families host on
// the same Redis resource. Both hosts MUST SetApplicationName the same value or
// Families cannot unprotect mint-side ciphertext. When no Redis connection string is
// present (non-Aspire local run / unit wiring tests) fall back to the default
// in-memory keyring while keeping the shared application name. A dedicated keyring
// multiplexer isolates the (rare) key-write traffic from the app's Aspire-managed
// cache connections; keys are cached in memory once loaded.
var deeplinkKeyring = builder.Services.AddDataProtection();
if (string.IsNullOrWhiteSpace(cacheConnectionString))
{
    deeplinkKeyring.SetApplicationName(DeepLinkConstants.ApplicationName);
}
else
{
    // Dedicated process-lifetime keyring multiplexer (same lifetime as the host).
    // ConnectAsync with AbortOnConnectFail=false so an unreachable Redis does not
    // hard-fail host startup — keyring access retries lazily once Redis returns.
    var keyringOptions = ConfigurationOptions.Parse(cacheConnectionString);
    keyringOptions.AbortOnConnectFail = false;
    var keyringMultiplexer = await ConnectionMultiplexer.ConnectAsync(keyringOptions);
    deeplinkKeyring
        .PersistKeysToStackExchangeRedis(keyringMultiplexer)
        .SetApplicationName(DeepLinkConstants.ApplicationName);
}
// Scoped token minter lives in Assignments.Core (IDeepLinkTokenMinter); the
// purpose-scoped protector is registered there alongside its services.

builder.Services.AddAssignmentsCore(builder.Configuration);
// C3 certificate rendering — the QuestPDF generator lives in the Api layer
// (the only runtime that dispatches finalize) and is registered here after
// AddAssignmentsCore so the Scrutor-scanned Core finalize handler resolves it.
builder.Services.AddTransient<SchoolCollab.Assignments.Core.Services.IAssignmentCertificateGenerator,
    SchoolCollab.Assignments.Api.Services.AssignmentCertificateGenerator>();
// Phase 2: register Settings.Core so IEntityCodeGenerator (auto-generated entity codes)
// is resolvable by the CreateAssignmentCommandHandler.
builder.Services.AddSettingsCore(builder.Configuration);

// Cross-bounded-context contact resolver (spec §9 G5): resolves subscribed
// contacts from the Students API. The named client is resolved via Aspire
// service discovery once the AppHost references students-api.
builder.Services.AddHttpClient("students-api");
builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.IContactResolver, SchoolCollab.Assignments.Api.Services.StudentsContactResolver>();

// Effective-policy resolver (notification-delivery-plan.md §3): reads the tenant
// default (Settings API) + grade override (Students API), merges at publish time.
// E3 (ar-19): relocated into Assignments.Core (Services/NotificationPolicyResolver.cs)
// so both the API and the Assignments.Worker sweeps resolve the same policy.
builder.Services.AddHttpClient("settings-api");
builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.INotificationPolicyResolver,
    SchoolCollab.Assignments.Core.Services.NotificationPolicyResolver>();
// Guardian-signature default resolver (WS-C1 / spec §7 Q1): reads the tenant
// default (Settings API) + grade override (Students API), resolves effective
// for the create-wizard pre-fill. Same named clients as the notification resolver.
builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.ISignatureDefaultResolver,
    SchoolCollab.Assignments.Api.Services.SignatureDefaultResolver>();

// Guardian sign-off consent language (WS-C1/C2 / spec §3.2 line 53): tenant
// consent-text override from the Settings API, fail-open to the embedded
// default. Same settings-api named client as the resolvers above.
builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.ISignatureConsentTextResolver,
    SchoolCollab.Assignments.Api.Services.SignatureConsentTextResolver>();
// Org-level AI-prompt lock resolver (WS-B2 / spec §3.4 line 70): whether the
// tenant LOCKED the org-level AI prompt, so the wizard's prompt override is
// disabled. Same settings-api named client; fail-open to false.
builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.IAiPromptPolicyResolver,
    SchoolCollab.Assignments.Api.Services.AiPromptPolicyResolver>();

// Student directory port for the sign-off surfaces (WS-C1 / spec §5 + §7 Q5):
// ward names + guardian links from the Students API.
builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.IStudentDirectory,
    SchoolCollab.Assignments.Api.Services.StudentDirectoryHttpClient>();

// Phase 3 (spec activity-group-enrollment.md FR-20..22): activity-group lookup
// port (Assignments → Students) for the link command and SelectedGroups publish.
builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.IActivityGroupLookup,
    SchoolCollab.Assignments.Api.Services.ActivityGroupLookupHttpClient>();

// Rev. 6 (spec activity-group-enrollment.md FR-58): subject/period consistency
// check at publish (Assignments → Students topic-assignment lookup).
builder.Services.AddScoped<SchoolCollab.Assignments.Core.Services.ITopicAssignmentLookup,
    SchoolCollab.Assignments.Api.Services.TopicAssignmentLookupHttpClient>();

builder.Services.AddOpenApi();

// WS-F3 (ar-15-signoff-relocation): the endpoint filter backing the public guardian
// sign-off group — validates the x-deeplink-token header against the shared keyring /
// purpose and cross-checks the recipient row server-side.
builder.Services.AddScoped<SchoolCollab.Assignments.Api.Auth.GuardianTokenEndpointFilter>();

// WS-A1 / decision (b): orphan sweep for staged uploads. The
// StagedFileSweepService is a hosted BackgroundService (sanctioned
// pre-worker hosted-service seam — no Assignments worker exists yet).
builder.Services.AddStagedFileSweep();

// WS-A2 / spec §3.5 step 8 / §7 Q6: scheduled-publish + archive
// sweeps. Same hosted-service seam — pure cores in Services/, hosted
// services here, Add{Layer}() extension in the api assembly.
builder.Services.AddAssignmentLifecycleSweeps();

// WS-E2 (ar-16): the notification delivery drain (hosted BackgroundService) +
// the Students-API contact-address resolver. Same hosted-service seam; the
// dedicated Assignments worker project is E3's deliverable.
builder.Services.AddNotificationDispatchSweep();

// Auth + tenancy (OIDC via Keycloak)
builder.Services.AddAuthAndTenancy(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.UseSerilogRequestLogging();

// All assignment endpoints require an authenticated user
var featureFlags = app.Services.GetRequiredService<IFeatureFlagService>();
// WS-F3 (ar-15-signoff-relocation): the public guardian sign-off route group,
// token-gated by GuardianTokenEndpointFilter (the shared ar-deeplink keyring/purpose).
// Mapped after the teacher/admin group so the /guardian namespace stays disjoint.
app.MapGuardianSignOffRoutes();
app.MapAssignmentEndpoints(featureFlags); 

app.Run();

// Makes Program accessible to WebApplicationFactory in integration tests
public partial class Program { }