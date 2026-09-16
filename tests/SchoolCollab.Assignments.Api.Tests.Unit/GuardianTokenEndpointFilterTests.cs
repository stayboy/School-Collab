using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Api.Auth;
using SchoolCollab.Assignments.Api.Endpoints;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SignOff;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-F3 (ar-15-signoff-relocation) — the token credential for the public guardian
/// sign-off route group. Exercises the full <c>x-deeplink-token</c> validation matrix
/// against a minimal TestServer host that maps ONLY <see cref="Endpoints.GuardianSignOffRoutes"/>
/// with a mocked <see cref="ISubmissionRepository"/>: valid token → passes and the
/// endpoint resolves the recipient's OwnerId; expired / tampered / missing → 401;
/// wrong assignment / wrong ward / tenant mismatch / absent row / non-guardian row /
/// stored-expiry-past → 403.
/// </summary>
[TestClass]
public class GuardianTokenEndpointFilterTests
{
    private static readonly Guid TenantId = Guid.Parse("bbbb1111-bbbb-1111-bbbb-111111111111");
    private static readonly Guid AssignmentId = Guid.Parse("aaaa2222-aaaa-2222-aaaa-222222222222");
    private static readonly Guid StudentId = Guid.Parse("cccc3333-cccc-3333-cccc-333333333333");
    private static readonly Guid OtherAssignmentId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid ContactId = Guid.Parse("dddd4444-dddd-4444-dddd-444444444444");
    private static readonly Guid GuardianId = Guid.Parse("eeee5555-eeee-5555-eeee-555555555555");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter<SignOffStateDto>(),
            new JsonStringEnumConverter<SignatureTypeDto>(),
        }
    };

    private sealed class PassFlag : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.EnableDeepLinks;
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default)
            => Task.FromResult(IsEnabled(featureKey));
    }

    private static AssignmentRecipient MakeRecipient(
        Guid tenantId, Guid ownerId, Guid? wardStudentId, DateTimeOffset? expiresAt,
        ContactOwnerType ownerType = ContactOwnerType.Guardian)
    {
        var recipient = AssignmentRecipient.Create(
            tenantId, AssignmentId, ownerType, ownerId, wardStudentId, ContactId,
            ContactChannel.Email, GuardianRole.Primary, true, true);
        // The filter cross-checks the AUTHORITATIVE stored expiry, so the row must
        // carry the deep link the publish handler would have attached.
        if (expiresAt is { } expiry)
        {
            recipient.AttachDeepLink("stored-deep-link-token", expiry);
        }
        return recipient;
    }

    private static SignOffContextDto SampleContext() =>
        new(AssignmentId, StudentId, "Assignment One", "Ward One", SignOffStateDto.AwaitingSignature,
            null, null, 2, 88m, true, "consent text", [new WardGuardianDto(GuardianId, "Jane Doe", true)]);

    private static SignOffStatusDto SampleStatus() =>
        new(StudentId, "Ward One", GuardianId, "Jane Doe", GuardianId, "Jane Doe",
            SignOffStateDto.Signed, DateTimeOffset.UtcNow, null, true, true, 2, 88m, true);

    /// <summary>
    /// Minimal TestServer host mapping ONLY <see cref="Endpoints.GuardianSignOffRoutes"/>.
    /// Every service the route delegates bind (<c>IQueryHandler</c>/
    /// <c>ICommandHandler</c> via <c>[FromServices]</c>) must be registered: minimal-API
    /// parameter binding resolves them BEFORE the endpoint filter pipeline runs, so an
    /// unregistered handler fails the request with a 500 rather than exercising the filter.
    /// </summary>
    private static async Task<(WebApplication App, HttpClient Client, DeepLinkProtector Protector)> BuildAsync(
        Func<AssignmentRecipient?> recipient,
        Mock<IFeatureFlagService>? flagMock = null,
        Action<SignOffSubmissionCommand>? onSignCommand = null,
        IReadOnlyCollection<Guid>? guardianOf = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(flagMock?.Object ?? new PassFlag());
        builder.Services.AddSingleton(new DeepLinkProtector(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()));
        builder.Services.AddSingleton<ITenantContextAccessor>(new TenantContextAccessor(new TenantProvider()));
        var repo = new Mock<ISubmissionRepository>();
        repo.Setup(r => r.GetRecipientAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recipient);
        builder.Services.AddSingleton(repo.Object);
        // P1-3 guardian-link authorization: the acting guardian (recipient OwnerId) must be
        // a linked guardian of the ROUTE student. A null set = the guardian is linked to
        // every route ward (the token's ContactId resolves to that guardian); an explicit set
        // restricts to those wards — used to prove a guardian of ward B cannot read ward C's
        // context/certificate while a multi-ward guardian CAN read both their own wards.
        var linkedStudents = guardianOf ?? new HashSet<Guid>();
        var directory = new Mock<IStudentDirectory>();
        directory.Setup(d => d.IsGuardianOfAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid studentId, Guid _, CancellationToken _1) =>
                guardianOf is null || linkedStudents.Contains(studentId));
        builder.Services.AddSingleton(directory.Object);
        // The certificate route binds an IFileStore via [FromServices] BEFORE the endpoint
        // filter runs (minimal-API parameter resolution precedes the filter pipeline), so
        // every host mapping the full guardian group must register it.
        builder.Services.AddSingleton(Mock.Of<SchoolCollab.Assignments.Core.Services.IFileStore>());
        var contextHandler = new Mock<IQueryHandler<GetSignOffContextQuery, SignOffContextDto>>();
        contextHandler.Setup(h => h.HandleAsync(It.IsAny<GetSignOffContextQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleContext());
        builder.Services.AddSingleton(contextHandler.Object);
        var signHandler = new Mock<ICommandHandler<SignOffSubmissionCommand, SignOffStatusDto>>();
        signHandler.Setup(h => h.HandleAsync(It.IsAny<SignOffSubmissionCommand>(), It.IsAny<CancellationToken>()))
            .Returns((SignOffSubmissionCommand c, CancellationToken _) =>
            {
                onSignCommand?.Invoke(c);
                return Task.FromResult(SampleStatus());
            });
        builder.Services.AddSingleton(signHandler.Object);
        builder.Services.AddScoped<GuardianTokenEndpointFilter>();
        builder.Services.AddLogging();
        // The POST body binds SignatureTypeDto from a string enum — the host must
        // carry the same converters the ApiClient serialises with.
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter<SignOffStateDto>());
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter<SignatureTypeDto>());
        });

        var app = builder.Build();
        app.MapGuardianSignOffRoutes();
        await app.StartAsync();
        var client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();
        var protector = app.Services.GetRequiredService<DeepLinkProtector>();
        return (app, client, protector);
    }

    private static string Mint(DeepLinkProtector p, Guid? wardStudentId = null, DateTimeOffset? expiresAt = null,
        Guid? assignmentId = null, int ownerType = 1) =>
        p.Protect(new DeepLinkTokenPayload(
            TenantId, assignmentId ?? AssignmentId, ContactId, ownerType, 0, wardStudentId ?? StudentId,
            expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(15)));

    [TestMethod]
    public async Task ValidToken_PassesAndEndpointResolvesGuardianId()
    {
        var now = DateTimeOffset.UtcNow;
        var recipient = MakeRecipient(TenantId, GuardianId, StudentId, now.AddDays(7));
        SignOffSubmissionCommand? captured = null;
        var (app, client, protector) = await BuildAsync(() => recipient, onSignCommand: c => captured = c);
        await using var _ = app;
        var token = Mint(protector);

        var contextResponse = await client.SendAsync(Get(token));
        contextResponse.StatusCode.Should().Be(HttpStatusCode.OK, "a valid token reaches the shared handler");

        // The POST proves the filter stashed the recipient-resolved GuardianId for the
        // endpoint to thread into the shared command.
        var signResponse = await client.SendAsync(Post(token));

        signResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.GuardianId.Should().Be(GuardianId,
            "the acting guardian is resolved server-side from the recipient row's OwnerId");
    }

    [TestMethod]
    public async Task MissingHeader_Returns401()
    {
        var (app, client, _) = await BuildAsync(() => MakeRecipient(TenantId, GuardianId, StudentId, DateTimeOffset.UtcNow.AddDays(7)));
        await using var _ = app;
        client.DefaultRequestHeaders.Remove("x-deeplink-token");

        var response = await client.SendAsync(Get());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task ExpiredToken_Returns401()
    {
        var (app, client, protector) = await BuildAsync(() => MakeRecipient(TenantId, GuardianId, StudentId, DateTimeOffset.UtcNow.AddDays(7)));
        await using var _ = app;
        var token = Mint(protector, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var response = await client.SendAsync(Get(token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task TamperedToken_Returns401()
    {
        var (app, client, _) = await BuildAsync(() => MakeRecipient(TenantId, GuardianId, StudentId, DateTimeOffset.UtcNow.AddDays(7)));
        await using var _ = app;

        var response = await client.SendAsync(Get("tampered-token"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task WrongAssignmentScope_Returns403()
    {
        var (app, client, protector) = await BuildAsync(() => MakeRecipient(TenantId, GuardianId, StudentId, DateTimeOffset.UtcNow.AddDays(7)));
        await using var _ = app;
        var token = Mint(protector, assignmentId: OtherAssignmentId);

        var response = await client.SendAsync(Get(token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task GuardianNotLinkedToRouteWard_Returns403()
    {
        // P1-3 adjudication: ward authorization is the guardian LINK to the ROUTE student,
        // not a token/row WardStudentId equality. A guardian linked to NO ward (guardianOf
        // empty) must be denied reading this ward's context.
        var (app, client, protector) = await BuildAsync(
            () => MakeRecipient(TenantId, GuardianId, StudentId, DateTimeOffset.UtcNow.AddDays(7)),
            guardianOf: Array.Empty<Guid>());
        await using var _ = app;
        var token = Mint(protector);

        var response = await client.SendAsync(Get(token, StudentId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task GuardianOfWardBNotWardC_CannotReadWardCContext_Returns403()
    {
        // The token's WardStudentId is URL-echoed (a non-authoritative field); the loaded row's
        // WardStudentId is never consulted. Read access is governed solely by whether the acting
        // guardian (recipient.OwnerId) is a linked guardian of the ROUTE student (ward C here).
        var wardB = Guid.Parse("aaaa0000-aaaa-0000-aaaa-000000000001");
        var wardC = Guid.Parse("aaaa0000-aaaa-0000-aaaa-000000000002");
        var (app, client, protector) = await BuildAsync(
            () => MakeRecipient(TenantId, GuardianId, StudentId, DateTimeOffset.UtcNow.AddDays(7)),
            guardianOf: new[] { wardB });
        await using var _ = app;
        // Mint with the token ward = ward C (URL-echoed) but the guardian is only linked to B.
        var token = Mint(protector, wardStudentId: wardC);

        var contextResponse = await client.SendAsync(Get(token, wardC));

        contextResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a guardian of ward B cannot read ward C's sign-off context by URL edit");
    }

    [TestMethod]
    public async Task GuardianOfWardBNotWardC_CannotReadWardCCertificate_Returns403()
    {
        var wardB = Guid.Parse("aaaa0000-aaaa-0000-aaaa-000000000003");
        var wardC = Guid.Parse("aaaa0000-aaaa-0000-aaaa-000000000004");
        var (app, client, protector) = await BuildAsync(
            () => MakeRecipient(TenantId, GuardianId, StudentId, DateTimeOffset.UtcNow.AddDays(7)),
            guardianOf: new[] { wardB });
        await using var _ = app;
        var token = Mint(protector, wardStudentId: wardC);

        var certResponse = await client.SendAsync(Certificate(token, wardC));

        certResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a guardian of ward B cannot read ward C's certificate by URL edit");
    }

    [TestMethod]
    public async Task MultiWardGuardian_CanReadBothOwnWardsContexts_ReturnsOk()
    {
        // A multi-ward guardian (linked to both ward A and ward B) may read BOTH of their own
        // wards' sign-off contexts — the FirstOrDefault-per-(assignment,contact) recipient row
        // must not 403 the second ward.
        var wardA = Guid.Parse("aaaa0000-aaaa-0000-aaaa-000000000005");
        var wardB = Guid.Parse("aaaa0000-aaaa-0000-aaaa-000000000006");
        var (app, client, protector) = await BuildAsync(
            () => MakeRecipient(TenantId, GuardianId, StudentId, DateTimeOffset.UtcNow.AddDays(7)),
            guardianOf: new[] { wardA, wardB });
        await using var _ = app;
        var token = Mint(protector, wardStudentId: wardA);

        var wardAResponse = await client.SendAsync(Get(token, wardA));
        var wardBResponse = await client.SendAsync(Get(token, wardB));

        wardAResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        wardBResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "a multi-ward guardian reads each of their own wards");
    }

    [TestMethod]
    public async Task TenantMismatch_Returns403()
    {
        var otherTenant = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var (app, client, protector) = await BuildAsync(() => MakeRecipient(otherTenant, GuardianId, StudentId, DateTimeOffset.UtcNow.AddDays(7)));
        await using var _ = app;
        var token = Mint(protector);

        var response = await client.SendAsync(Get(token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task RecipientAbsent_Returns403()
    {
        var (app, client, protector) = await BuildAsync(() => null);
        await using var _ = app;
        var token = Mint(protector);

        var response = await client.SendAsync(Get(token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task NonGuardianRecipient_Returns403()
    {
        var (app, client, protector) = await BuildAsync(
            () => MakeRecipient(TenantId, StudentId, StudentId, DateTimeOffset.UtcNow.AddDays(7), ownerType: ContactOwnerType.Student));
        await using var _ = app;
        var token = Mint(protector, ownerType: 0);

        var response = await client.SendAsync(Get(token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task StoredExpiryPast_Returns403_EvenWithFreshHeaderToken()
    {
        var fresh = MakeRecipient(TenantId, GuardianId, StudentId, DateTimeOffset.UtcNow.AddDays(-1));
        var (app, client, protector) = await BuildAsync(() => fresh);
        await using var _ = app;
        var token = Mint(protector, expiresAt: DateTimeOffset.UtcNow.AddMinutes(15));

        var response = await client.SendAsync(Get(token));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task WrongPurposeToken_Returns401()
    {
        var (app, client, _) = await BuildAsync(() => MakeRecipient(TenantId, GuardianId, StudentId, DateTimeOffset.UtcNow.AddDays(7)));
        await using var _ = app;
        // Same payload shape, different DataProtection purpose — the token must not be
        // accepted outside the shared ar-deeplink purpose.
        var foreignPurpose = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()
            .CreateProtector("some-other-purpose");
        var payload = new DeepLinkTokenPayload(TenantId, AssignmentId, ContactId, 1, 0, StudentId,
            DateTimeOffset.UtcNow.AddMinutes(15));
        var token = Convert.ToBase64String(foreignPurpose.Protect(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));

        var response = await client.SendAsync(Get(token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task FlagOff_Returns401()
    {
        var flagMock = new Mock<IFeatureFlagService>();
        flagMock.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.EnableDeepLinks, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var (app, client, protector) = await BuildAsync(
            () => MakeRecipient(TenantId, GuardianId, StudentId, DateTimeOffset.UtcNow.AddDays(7)), flagMock);
        await using var _ = app;
        var token = Mint(protector);

        var response = await client.SendAsync(Get(token));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static HttpRequestMessage Get(string? token = null, Guid? studentId = null)
    {
        var sid = studentId ?? StudentId;
        var msg = new HttpRequestMessage(HttpMethod.Get,
            $"/guardian/assignments/{AssignmentId}/students/{sid}/sign-off");
        if (token is not null)
        {
            msg.Headers.TryAddWithoutValidation("x-deeplink-token", token);
        }
        return msg;
    }

    private static HttpRequestMessage Certificate(string? token, Guid? studentId = null)
    {
        var sid = studentId ?? StudentId;
        var msg = new HttpRequestMessage(HttpMethod.Get,
            $"/guardian/assignments/{AssignmentId}/students/{sid}/certificate");
        if (token is not null)
        {
            msg.Headers.TryAddWithoutValidation("x-deeplink-token", token);
        }
        return msg;
    }

    private static HttpRequestMessage Post(string? token)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post,
            $"/guardian/assignments/{AssignmentId}/students/{StudentId}/sign-off")
        {
            Content = JsonContent.Create(
                new GuardianSignOffSubmissionRequest(SignatureTypeDto.Typed, "Jane Doe"),
                options: JsonOptions)
        };
        if (token is not null)
        {
            msg.Headers.TryAddWithoutValidation("x-deeplink-token", token);
        }
        return msg;
    }
}
