using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Application.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>media-pill-listing</c> (Q8/Slice D/F) — <c>AssignmentsApiClient.GetInstructionMediaStreamAsync</c>
/// against a scripted <see cref="HttpMessageHandler"/>.
///
/// <para><b>Discriminating:</b> pre-fix the method does not exist; and a <c>byte[]</c>-based
/// implementation (the ward client's transport) would fail the stream assertion below — the round
/// deliberately hands the content on as a <see cref="Stream"/> so it never materialises as an
/// oversized interop array.</para>
/// </summary>
[TestClass]
public class AssignmentsApiClientInstructionMediaTests
{
    /// <summary>Scripts one response and records the request URIs the client actually issued.</summary>
    private sealed class ScriptedHandler(HttpStatusCode status, string? body) : HttpMessageHandler
    {
        public List<Uri> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);
            var response = new HttpResponseMessage(status);
            if (body is not null)
            {
                response.Content = new StringContent(body);
            }

            return Task.FromResult(response);
        }
    }

    private static (AssignmentsApiClient Client, ScriptedHandler Handler) Client(
        HttpStatusCode status, string? body = null)
    {
        var handler = new ScriptedHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        return (new AssignmentsApiClient(http, NullLogger<AssignmentsApiClient>.Instance), handler);
    }

    [TestMethod]
    public async Task GetInstructionMediaStreamAsync_404_ReturnsNull_AndSuccessStreams()
    {
        var assignmentId = Guid.NewGuid();
        var instructionId = Guid.NewGuid();

        // 404 (missing assignment/instruction/file) is a null, never an exception.
        var (notFound, notFoundHandler) = Client(HttpStatusCode.NotFound);
        var missing = await notFound.GetInstructionMediaStreamAsync(assignmentId, instructionId);

        missing.Should().BeNull("a 404 is the missing-row/file signal the list surfaces inline");
        notFoundHandler.RequestedUris.Should().ContainSingle()
            .Which.AbsolutePath.Should().Be($"/assignments/{assignmentId}/materials/{instructionId}",
                "the client addresses the teacher route (no studentId)");

        // A success returns the CONTENT STREAM — never a byte[] materialised here.
        var (found, _) = Client(HttpStatusCode.OK, "video-bytes");
        var stream = await found.GetInstructionMediaStreamAsync(assignmentId, instructionId);

        stream.Should().NotBeNull("a 200 exposes the response stream");
        using var reader = new StreamReader(stream!);
        (await reader.ReadToEndAsync()).Should().Be("video-bytes",
            "the bytes stream through — a byte[]-based implementation would not satisfy this pin");
    }
}
