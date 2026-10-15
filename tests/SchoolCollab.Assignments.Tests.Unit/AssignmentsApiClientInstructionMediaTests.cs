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
    /// <summary>Scripts one response and records the request URIs the client actually issued — plus
    /// the outgoing staged file part's media type, which the multipart Content-Type pins read.</summary>
    private sealed class ScriptedHandler(HttpStatusCode status, string? body) : HttpMessageHandler
    {
        public List<Uri> RequestedUris { get; } = [];

        /// <summary>The BARE media type the client put on the staged file part, or null when the
        /// request carried no multipart file part.</summary>
        public string? StagedContentTypeMediaType { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);

            // The custom handler sees the content UNserialised, so the parts and their headers are
            // directly inspectable — no multipart parsing needed.
            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                {
                    if (part.Headers.ContentType is { } partType)
                    {
                        StagedContentTypeMediaType = partType.MediaType;
                    }
                }
            }

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

    /// <summary>Live defect 2026-10-15 (recorder → metadata dialog): a MediaRecorder take reports a
    /// MIME type WITH PARAMETERS (<c>audio/mp4;codecs=opus</c>). The <c>MediaTypeHeaderValue</c>
    /// constructor accepts a bare type only, so the client threw <see cref="FormatException"/>
    /// before sending anything — the take was dropped with nothing shown to the author.
    ///
    /// <para><b>Discriminating:</b> pre-fix this call throws before the request is issued; post-fix
    /// the take reaches the server carrying a BARE media type, which is what the served-media route
    /// (<c>Results.Stream</c>) and the kind inference both assume.</para></summary>
    [TestMethod]
    public async Task StageAttachmentAsync_ParameterisedMimeType_StagesAsBareMediaType()
    {
        var (client, handler) = Client(
            HttpStatusCode.OK,
            """{"fileName":"take.webm","contentType":"audio/webm","fileSize":4,"storagePath":"p"}""");

        using var take = new MemoryStream([1, 2, 3, 4]);
        var staged = await client.StageAttachmentAsync(take, "take.webm", "audio/mp4;codecs=opus", 4);

        staged.StoragePath.Should().Be("p",
            "the take must reach the server — pre-fix this call threw FormatException before sending");
        handler.RequestedUris.Should().ContainSingle()
            .Which.AbsolutePath.Should().Be("/assignments/attachments/stage");
        handler.StagedContentTypeMediaType.Should().Be("audio/mp4",
            "the staged part carries the BARE media type — the ';codecs=opus' parameter is dropped, "
            + "never stored (the served-media route hands it straight to Results.Stream)");
    }

    /// <summary>The fail-open half of the same rule: a MIME type the browser invented must never be
    /// able to break staging — unparseable input degrades to octet-stream instead of throwing.</summary>
    [TestMethod]
    public async Task StageAttachmentAsync_UnparseableMimeType_DegradesToOctetStream()
    {
        var (client, handler) = Client(
            HttpStatusCode.OK,
            """{"fileName":"odd.bin","contentType":"application/octet-stream","fileSize":2,"storagePath":"q"}""");

        using var take = new MemoryStream([7, 8]);
        var staged = await client.StageAttachmentAsync(take, "odd.bin", "not a media type", 2);

        staged.StoragePath.Should().Be("q", "an unparseable type still stages");
        handler.StagedContentTypeMediaType.Should().Be("application/octet-stream",
            "the invalid value is never forwarded as a header");
    }
}
