using System.Net;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Round <c>media-pill-listing</c> (Q9/Slice C) — the TEACHER media route
/// <c>GET /assignments/{id}/materials/{instructionId}</c>, the ward route's twin minus
/// <c>studentId</c>. Exercised against the same container-free TestServer harness as
/// <see cref="WardRoutesTests"/> with mocked handlers (Program never starts).
///
/// <para><b>Discriminating:</b> pre-fix the route does not exist, so every case 404s.</para>
/// </summary>
[TestClass]
public class TeacherMaterialRouteTests
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string MaterialUrl(Guid instructionId) =>
        $"/assignments/{AssignmentId}/materials/{instructionId}";

    private sealed class StubFeatureFlags(bool disableOidcAuth) : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.DisableOIDCAuth && disableOidcAuth;
        public IDictionary<string, bool> GetAllFlags() =>
            new Dictionary<string, bool> { [FeatureFlagKeys.DisableOIDCAuth] = disableOidcAuth };
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsEnabled(featureKey));
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private static async Task<WebApplication> StartHostAsync(
        bool disableOidcAuth = true,
        Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags(disableOidcAuth));
        if (!disableOidcAuth)
        {
            // Real-auth posture: the group's RequireAuthorization() needs a scheme to challenge with,
            // so the anonymous caller gets a 401 rather than a DI failure.
            builder.Services.AddAuthentication(AuthTenancyExtensions.BearerScheme)
                .AddScheme<AuthenticationSchemeOptions, AnonymousRefusingAuthHandler>(
                    AuthTenancyExtensions.BearerScheme, _ => { });
            builder.Services.AddAuthorization();
        }

        configure?.Invoke(builder.Services);

        var app = builder.Build();
        if (!disableOidcAuth)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();
        return app;
    }

    /// <summary>No principal ever authenticates: only the anonymous challenge path is under test.</summary>
    private sealed class AnonymousRefusingAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }

    private static HttpClient CreateClient(WebApplication app)
        => ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

    private static (Assignment Assignment, Guid InstructionId) AssignmentWithVideoInstruction()
    {
        var assignment = Assignment.Create(
            "Algebra", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, Guid.NewGuid(), null, null, Guid.NewGuid());
        assignment.AddInstructionItems(
        [
            (null, InstructionKind.Video, null, null, "lecture.webm", "video/webm", 1024, "staged/lecture.webm", "Intro lecture", null)
        ]);
        return (assignment, assignment.InstructionItems[0].Id);
    }

    private sealed class StubFileStore(string? content) : IFileStore
    {
        public Task<string> StoreAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
            => Task.FromResult("stored");

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            if (content is null)
            {
                throw new FileNotFoundException(storagePath);
            }
            Stream s = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            return Task.FromResult(s);
        }

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [TestMethod]
    public async Task TeacherMaterial_Returns200_WithStoredContentType_AndRangeProcessing()
    {
        // Q9/AC-7: the route streams the staged file with its stored content type and advertises
        // Accept-Ranges (enableRangeProcessing) so <audio>/<video> are seekable.
        var (assignment, instructionId) = AssignmentWithVideoInstruction();
        var repo = new Mock<IAssignmentRepository>();
        repo.Setup(r => r.GetAsync(AssignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(assignment);

        await using var app = await StartHostAsync(configure: s =>
        {
            s.AddSingleton(repo.Object);
            s.AddSingleton<IFileStore>(new StubFileStore("video-bytes"));
        });
        var client = CreateClient(app);

        var response = await client.GetAsync(MaterialUrl(instructionId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("video/webm",
            "the response type is the instruction's stored content type");
        response.Headers.TryGetValues("Accept-Ranges", out var ranges).Should().BeTrue(
            "range processing is enabled so the player can seek");
        ranges!.Should().Contain("bytes");
        (await response.Content.ReadAsStringAsync()).Should().Be("video-bytes");
    }

    [TestMethod]
    public async Task TeacherMaterial_Returns404_WhenInstructionMissing_OrNotMedia_OrFileGone()
    {
        var (assignment, instructionId) = AssignmentWithVideoInstruction();
        var repo = new Mock<IAssignmentRepository>();
        repo.Setup(r => r.GetAsync(AssignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(assignment);

        // (a) an unknown instruction id, (b) a missing assignment, (c) a stored reference whose file
        // is gone — each is a 404, never a stream of the wrong file and never an exception.
        await using (var app = await StartHostAsync(configure: s =>
        {
            s.AddSingleton(repo.Object);
            s.AddSingleton<IFileStore>(new StubFileStore("video-bytes"));
        }))
        {
            var client = CreateClient(app);
            (await client.GetAsync(MaterialUrl(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        var missingRepo = new Mock<IAssignmentRepository>();
        missingRepo.Setup(r => r.GetAsync(AssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Assignment?)null);
        await using (var app = await StartHostAsync(configure: s =>
        {
            s.AddSingleton(missingRepo.Object);
            s.AddSingleton<IFileStore>(new StubFileStore("video-bytes"));
        }))
        {
            var client = CreateClient(app);
            (await client.GetAsync(MaterialUrl(instructionId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        await using (var app = await StartHostAsync(configure: s =>
        {
            s.AddSingleton(repo.Object);
            s.AddSingleton<IFileStore>(new StubFileStore(null));
        }))
        {
            var client = CreateClient(app);
            (await client.GetAsync(MaterialUrl(instructionId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [TestMethod]
    public async Task TeacherMaterial_RequiresAuthentication()
    {
        // AC-7: the route inherits the /assignments group's bearer posture — an anonymous caller is
        // challenged (401) rather than served, exactly as for the ward media route it mirrors.
        var (assignment, instructionId) = AssignmentWithVideoInstruction();
        var repo = new Mock<IAssignmentRepository>();
        repo.Setup(r => r.GetAsync(AssignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(assignment);

        await using var app = await StartHostAsync(disableOidcAuth: false, configure: s =>
        {
            s.AddSingleton(repo.Object);
            s.AddSingleton<IFileStore>(new StubFileStore("video-bytes"));
        });
        var client = CreateClient(app);

        var response = await client.GetAsync(MaterialUrl(instructionId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
