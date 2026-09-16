using System.Net;
using System.Net.Http.Json;
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
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-F3 (ar-15-signoff-relocation) — the public guardian sign-off route group
/// (decision (a)/(b)/(d)) against a minimal TestServer host that maps ONLY
/// <see cref="GuardianSignOffRoutes"/> with mocked handlers + the token filter.
/// Asserts: the group reuses the SAME <see cref="GetSignOffContextQuery"/> /
/// <see cref="SignOffSubmissionCommand"/> handlers; the sign POST threads the
/// token-resolved GuardianId from <see cref="HttpContext.Items"/> (NEVER a
/// body-supplied one); error-map parity with the teacher sign route; reassign /
/// finalize absent from the group; and the certificate route 404 parity.
/// </summary>
[TestClass]
public class GuardianSignOffRoutesTests
{
    private static readonly Guid TenantId = Guid.Parse("11112222-aaaa-1111-aaaa-111111111111");
    private static readonly Guid AssignmentId = Guid.Parse("aaaaaaaa-bbbb-cccc-aaaa-111111111111");
    private static readonly Guid StudentId = Guid.Parse("bbbbbbbb-cccc-dddd-bbbb-222222222222");
    private static readonly Guid ContactId = Guid.Parse("cccccccc-dddd-eeee-cccc-333333333333");
    private static readonly Guid GuardianId = Guid.Parse("dddddddd-eeee-ffff-dddd-444444444444");

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(
        System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter<SignOffStateDto>(),
            new System.Text.Json.Serialization.JsonStringEnumConverter<SignatureTypeDto>(),
        }
    };

    private async Task<(WebApplication App, HttpClient Client, DeepLinkProtector Protector)> BuildAsync(
        Mock<ICommandHandler<SignOffSubmissionCommand, SignOffStatusDto>>? signHandler,
        Mock<IQueryHandler<GetSignOffContextQuery, SignOffContextDto>>? contextHandler = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new PassFlag());
        builder.Services.AddSingleton(new DeepLinkProtector(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()));
        builder.Services.AddSingleton<ITenantContextAccessor>(new TenantContextAccessor(new TenantProvider()));
        var repo = new Mock<ISubmissionRepository>();
        repo.Setup(r => r.GetRecipientAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Recipient());
        builder.Services.AddSingleton(repo.Object);
        // P1-3 - the guardian-link authorization needs the directory port; the guardian
        // (recipient OwnerId) is linked to the route ward by default in this group's tests.
        var directory = new Mock<IStudentDirectory>();
        directory.Setup(d => d.IsGuardianOfAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        builder.Services.AddSingleton(directory.Object);
        if (signHandler is not null) builder.Services.AddSingleton(signHandler.Object);
        if (contextHandler is not null) builder.Services.AddSingleton(contextHandler.Object);
        builder.Services.AddScoped<GuardianTokenEndpointFilter>();
        builder.Services.AddLogging();
        // SignatureTypeDto binds from a string enum in the POST body — without the
        // converter the request fails with 400 during binding, BEFORE the filter runs.
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter<SignOffStateDto>());
            o.SerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter<SignatureTypeDto>());
        });

        var app = builder.Build();
        app.MapGuardianSignOffRoutes();
        await app.StartAsync();
        var client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();
        var protector = app.Services.GetRequiredService<DeepLinkProtector>();
        return (app, client, protector);
    }

    private static AssignmentRecipient Recipient()
    {
        var recipient = AssignmentRecipient.Create(TenantId, AssignmentId, ContactOwnerType.Guardian, GuardianId,
            StudentId, ContactId, ContactChannel.Email, GuardianRole.Primary, true, true);
        // The filter cross-checks the AUTHORITATIVE stored expiry on the recipient row.
        recipient.AttachDeepLink("stored-deep-link-token", DateTimeOffset.UtcNow.AddDays(7));
        return recipient;
    }

    private static string Mint(DeepLinkProtector p) =>
        p.Protect(new DeepLinkTokenPayload(TenantId, AssignmentId, ContactId, 1, 0, StudentId,
            DateTimeOffset.UtcNow.AddMinutes(15)));

    private static SignOffStatusDto SampleStatus() =>
        new(StudentId, "Ward One", GuardianId, "Jane Doe", GuardianId, "Jane Doe",
            SignOffStateDto.Signed, DateTimeOffset.UtcNow, null, true, true, 2, 88m, true);

    private static SignOffContextDto SampleContext() =>
        new(AssignmentId, StudentId, "Assignment One", "Ward One", SignOffStateDto.AwaitingSignature,
            null, null, 2, 88m, true, "consent text", [new WardGuardianDto(GuardianId, "Jane Doe", true)]);

    private sealed class PassFlag : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.EnableDeepLinks;
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default)
            => Task.FromResult(IsEnabled(featureKey));
    }

    [TestMethod]
    public async Task SignOff_ReusesSharedHandler_ThreadsTokenGuardianId_NotBodyGuardianId()
    {
        SignOffSubmissionCommand? captured = null;
        var signHandler = new Mock<ICommandHandler<SignOffSubmissionCommand, SignOffStatusDto>>();
        signHandler.Setup(h => h.HandleAsync(It.IsAny<SignOffSubmissionCommand>(), It.IsAny<CancellationToken>()))
            .Returns((SignOffSubmissionCommand c, CancellationToken _) =>
            {
                captured = c;
                return Task.FromResult(SampleStatus());
            });

        var (app, client, protector) = await BuildAsync(signHandler);
        await using var _ = app;
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/guardian/assignments/{AssignmentId}/students/{StudentId}/sign-off")
        {
            Content = JsonContent.Create(new GuardianSignOffSubmissionRequest(SignatureTypeDto.Typed, "Jane Doe"),
                options: JsonOptions)
        };
        req.Headers.TryAddWithoutValidation("x-deeplink-token", Mint(protector));
        req.Headers.UserAgent.ParseAdd("unit-test-agent/1.0");

        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.AssignmentId.Should().Be(AssignmentId);
        captured.StudentId.Should().Be(StudentId);
        // The acting guardian MUST come from the token-resolved recipient OwnerId, never a body field.
        captured.GuardianId.Should().Be(GuardianId, "the token identifies the signer — the body must not carry GuardianId");
    }

    [TestMethod]
    public async Task SignOff_MapsErrors_WithTeacherRouteParity()
    {
        (HttpStatusCode Expected, Exception Error)[] cases =
        {
            (HttpStatusCode.NotFound, new AssignmentNotFoundException(AssignmentId)),
            (HttpStatusCode.NotFound, new SubmissionNotFoundException(StudentId)),
            (HttpStatusCode.Conflict, new SubmissionAlreadySignedException(StudentId)),
            (HttpStatusCode.Conflict, new SubmissionSignOffStateException("state")),
            (HttpStatusCode.Conflict, new SubmissionLockedException(StudentId)),
            (HttpStatusCode.Conflict, new GuardianNotAuthorizedException(StudentId, GuardianId)),
            (HttpStatusCode.BadRequest, new ArgumentException("A typed signature is required.")),
        };

        foreach (var (expected, error) in cases)
        {
            var signHandler = new Mock<ICommandHandler<SignOffSubmissionCommand, SignOffStatusDto>>();
            signHandler.Setup(h => h.HandleAsync(It.IsAny<SignOffSubmissionCommand>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromException<SignOffStatusDto>(error));

            var (app, client, protector) = await BuildAsync(signHandler);
            await using var _ = app;
            var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"/guardian/assignments/{AssignmentId}/students/{StudentId}/sign-off")
            {
                Content = JsonContent.Create(new GuardianSignOffSubmissionRequest(SignatureTypeDto.Click),
                    options: JsonOptions)
            };
            req.Headers.TryAddWithoutValidation("x-deeplink-token", Mint(protector));

            var response = await client.SendAsync(req);

            response.StatusCode.Should().Be(expected, "guardian route maps {0} identically to the teacher route",
                error.GetType().Name);
        }
    }

    [TestMethod]
    public async Task Context_ReusesSharedHandler()
    {
        var contextHandler = new Mock<IQueryHandler<GetSignOffContextQuery, SignOffContextDto>>();
        contextHandler.Setup(h => h.HandleAsync(It.IsAny<GetSignOffContextQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleContext());

        var (app, client, protector) = await BuildAsync(signHandler: null, contextHandler);
        await using var _ = app;
        var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"/guardian/assignments/{AssignmentId}/students/{StudentId}/sign-off");
        req.Headers.TryAddWithoutValidation("x-deeplink-token", Mint(protector));

        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task ReassignAndFinalize_NotMapped_OnGuardianGroup()
    {
        var (app, client, protector) = await BuildAsync(signHandler: null);
        await using var _ = app;

        foreach (var path in new[] { "/sign-off/reassign", "/sign-off/finalize" })
        {
            var reassign = new HttpRequestMessage(
                HttpMethod.Post,
                $"/guardian/assignments/{AssignmentId}/students/{StudentId}{path}");
            reassign.Headers.TryAddWithoutValidation("x-deeplink-token", Mint(protector));

            var response = await client.SendAsync(reassign);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "{0} is teacher-only and must not exist on the guardian group", path);
        }
    }

    [TestMethod]
    public async Task Certificate_NoSignatureEvent_Returns404()
    {
        var repo = new Mock<ISubmissionRepository>();
        repo.Setup(r => r.GetRecipientAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Recipient());
        repo.Setup(r => r.GetSignatureEventByAssignmentStudentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => default(SignatureEvent?));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new PassFlag());
        builder.Services.AddSingleton(new DeepLinkProtector(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()));
        builder.Services.AddSingleton<ITenantContextAccessor>(new TenantContextAccessor(new TenantProvider()));
        builder.Services.AddSingleton(repo.Object);
        // P1-3 - the certificate route runs the same guardian-link authorization.
        var directory = new Mock<IStudentDirectory>();
        directory.Setup(d => d.IsGuardianOfAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        builder.Services.AddSingleton(directory.Object);
        builder.Services.AddSingleton(Mock.Of<IFileStore>());
        builder.Services.AddScoped<GuardianTokenEndpointFilter>();
        builder.Services.AddLogging();
        var app = builder.Build();
        app.MapGuardianSignOffRoutes();
        await app.StartAsync();
        await using var _ = app;
        var client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();
        var protector = app.Services.GetRequiredService<DeepLinkProtector>();
        var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"/guardian/assignments/{AssignmentId}/students/{StudentId}/certificate");
        req.Headers.TryAddWithoutValidation("x-deeplink-token", Mint(protector));

        var response = await client.SendAsync(req);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
