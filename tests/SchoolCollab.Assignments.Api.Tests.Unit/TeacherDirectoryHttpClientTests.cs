using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Api.Services;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// ar-24 AC5 (row 7) — strict-2xx regression coverage for
/// <c>TeacherDirectoryHttpClient.ExistsAsync</c>. A scripted 302 / 404 must be
/// treated as NOT-exists, and a 200 as exists. NOTE: a scripted handler never
/// follows redirects, so this test cannot by itself discriminate
/// <c>AllowAutoRedirect=false</c> — that is the ar-24 guard's source-scan assertion
/// (P1-6); this is the strict-status logic regression coverage.
/// </summary>
[TestClass]
public class TeacherDirectoryHttpClientTests
{
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static TeacherDirectoryHttpClient Create(HttpStatusCode status)
    {
        var handler = new MockHttpMessageHandler();
        handler.When("*/teachers/*").Respond(status);

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("students-api"))
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("http://students-api") });
        return new TeacherDirectoryHttpClient(factory.Object, NullLogger<TeacherDirectoryHttpClient>.Instance);
    }

    [TestMethod]
    public async Task ExistsAsync_Redirect302_isFalse() =>
        (await Create(HttpStatusCode.Redirect).ExistsAsync(TeacherId, CancellationToken.None)).Should().BeFalse();

    [TestMethod]
    public async Task ExistsAsync_NotFound404_isFalse() =>
        (await Create(HttpStatusCode.NotFound).ExistsAsync(TeacherId, CancellationToken.None)).Should().BeFalse();

    [TestMethod]
    public async Task ExistsAsync_Ok200_isTrue() =>
        (await Create(HttpStatusCode.OK).ExistsAsync(TeacherId, CancellationToken.None)).Should().BeTrue();
}
