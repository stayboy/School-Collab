using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SchoolCollab.Assignments.Api;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// C3 certificate GET route (decision (e)) — exercised against the same minimal
/// TestServer harness as <see cref="SignOffRoutesTests"/> (Program never
/// starts — <c>MapAssignmentEndpoints</c> only, with mocked
/// <see cref="ISubmissionRepository"/> + <see cref="IFileStore"/>). Covers the
/// streamed 200 and the two 404 paths.
/// </summary>
[TestClass]
public class CertificateRoutesTests
{
    private static readonly Guid AssignmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SignerGuardianId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static string CertificateUrl => $"/assignments/{AssignmentId}/students/{StudentId}/certificate";

    private static async Task<WebApplication> StartHostAsync(
        Mock<ISubmissionRepository>? submissionRepo = null,
        Mock<IFileStore>? fileStore = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());

        builder.Services.AddSingleton(submissionRepo?.Object ?? new Mock<ISubmissionRepository> { DefaultValue = DefaultValue.Mock }.Object);
        builder.Services.AddSingleton(fileStore?.Object ?? new Mock<IFileStore>().Object);

        var app = builder.Build();
        app.MapAssignmentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
        => ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

    private static SignatureEvent EventWithCertificate(string? path)
    {
        var e = SignatureEvent.Create(
            Guid.Parse("00000000-0000-0000-0000-00000000ca11"),
            AssignmentId, StudentId, SignerGuardianId,
            SignatureType.Typed, "Jane Doe", "203.0.113.7", "unit-test", "By signing I consent.");
        if (path is not null)
        {
            e.AttachCertificate(path);
        }
        return e;
    }

    [TestMethod]
    public async Task GetCertificate_200_StreamsPdf()
    {
        var repo = new Mock<ISubmissionRepository>();
        repo.Setup(r => r.GetSignatureEventByAssignmentStudentAsync(AssignmentId, StudentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventWithCertificate("uploads/signoff/cert.pdf"));
        var store = new Mock<IFileStore>();
        store.Setup(s => s.OpenReadAsync("uploads/signoff/cert.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.7 fake certificate content")));

        await using var app = await StartHostAsync(repo, store);
        var client = CreateClient(app);

        var response = await client.GetAsync(CertificateUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be(
            $"certificate-{AssignmentId:N}-{StudentId:N}.pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(bytes).Should().StartWith("%PDF", "the stored file bytes are streamed");
    }

    [TestMethod]
    public async Task GetCertificate_404_WhenNoEvent()
    {
        var repo = new Mock<ISubmissionRepository>();
        repo.Setup(r => r.GetSignatureEventByAssignmentStudentAsync(AssignmentId, StudentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SignatureEvent?)null);

        await using var app = await StartHostAsync(repo);
        var client = CreateClient(app);

        var response = await client.GetAsync(CertificateUrl);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task GetCertificate_404_WhenCertificateNotGenerated()
    {
        var repo = new Mock<ISubmissionRepository>();
        repo.Setup(r => r.GetSignatureEventByAssignmentStudentAsync(AssignmentId, StudentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventWithCertificate(path: null));

        await using var app = await StartHostAsync(repo);
        var client = CreateClient(app);

        var response = await client.GetAsync(CertificateUrl);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a signed-but-never-finalized pair has no certificate to download");
    }

    [TestMethod]
    public async Task GetCertificate_404_WhenFileMissing()
    {
        var repo = new Mock<ISubmissionRepository>();
        repo.Setup(r => r.GetSignatureEventByAssignmentStudentAsync(AssignmentId, StudentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EventWithCertificate("uploads/signoff/cert.pdf"));
        var store = new Mock<IFileStore>();
        store.Setup(s => s.OpenReadAsync("uploads/signoff/cert.pdf", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException());

        await using var app = await StartHostAsync(repo, store);
        var client = CreateClient(app);

        var response = await client.GetAsync(CertificateUrl);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a stored reference whose backing file is gone still 404s");
    }

    /// <summary>OIDC auth is disabled so the mapped group needs no authorization
    /// scheme (the SignOffRoutesTests posture).</summary>
    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.DisableOIDCAuth;

        public IDictionary<string, bool> GetAllFlags() =>
            new Dictionary<string, bool> { [FeatureFlagKeys.DisableOIDCAuth] = true };

        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(IsEnabled(featureKey));
    }
}
