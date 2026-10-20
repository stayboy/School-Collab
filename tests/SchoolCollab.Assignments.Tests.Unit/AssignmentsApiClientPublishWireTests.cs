using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round notification-templates-b2b (spec §3.7/§3.8) — the publish-dialog wire seam of
/// <see cref="AssignmentsApiClient"/> (the <see cref="AssignmentsApiClientInstructionMediaTests"/>
/// stub-handler precedent):
/// <list type="bullet">
///   <item>the new publish overload actually fills <c>Channels</c> and both template ids — the last
///         inert field set of the B2a wire;</item>
///   <item>its null case writes EXPLICIT JSON NULLS (the shared serializer options carry no
///         <c>WhenWritingNull</c> condition, and this round deliberately does not add one);</item>
///   <item><c>GetPublishOptionsAsync</c> is fail-open on every failure class — a transport throw
///         included — while a genuine caller cancellation still propagates;</item>
///   <item>the one-argument quick publish still posts a null body (it must keep reusing the stored
///         stamps).</item>
/// </list>
/// </summary>
[TestClass]
public class AssignmentsApiClientPublishWireTests
{
    /// <summary>Scripts one response and captures the request body the client actually sent.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<Uri> RequestedUris { get; } = [];

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);
            if (request.Content is not null)
            {
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return _respond(request);
        }
    }

    /// <summary>A handler whose transport always throws — what an unreachable API looks like.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private static (AssignmentsApiClient Client, CapturingHandler Handler) Client(
        HttpStatusCode status = HttpStatusCode.NoContent, string? body = null)
    {
        var handler = new CapturingHandler(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (body is not null)
            {
                // The JSON content type matters: the client's read would otherwise reject the body on
                // the media type alone and the case under test would never be reached.
                response.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            }

            return response;
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        return (new AssignmentsApiClient(http, NullLogger<AssignmentsApiClient>.Instance), handler);
    }

    private static JsonElement SentBody(CapturingHandler handler)
    {
        handler.RequestBody.Should().NotBeNull("the publish request always posts a body");
        return JsonDocument.Parse(handler.RequestBody!).RootElement;
    }

    // ── the new overload ─────────────────────────────────────────────────────

    /// <summary>The dialog's confirmed choices reach the request body: medium + both stamp ids.</summary>
    [TestMethod]
    public async Task Publish_WithChoices_CarriesChannelsAndBothTemplateIds()
    {
        var id = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var emailTemplateId = Guid.NewGuid();
        var smsTemplateId = Guid.NewGuid();
        var (client, handler) = Client();

        await client.PublishAsync(
            id, [contactId], PublishChannel.SmsOnly, emailTemplateId, smsTemplateId);

        handler.RequestedUris.Should().ContainSingle()
            .Which.AbsolutePath.Should().Be($"/assignments/{id}/publish");

        var body = SentBody(handler);
        body.GetProperty("contactIds").EnumerateArray().Select(c => c.GetGuid())
            .Should().Equal([contactId]);
        body.GetProperty("channels").GetInt32().Should().Be((int)PublishChannel.SmsOnly,
            "PublishChannel is an int-valued contract, so its number is the wire form");
        body.GetProperty("publishEmailTemplateId").GetGuid().Should().Be(emailTemplateId);
        body.GetProperty("publishSmsTemplateId").GetGuid().Should().Be(smsTemplateId);
    }

    /// <summary>Null choices stay EXPLICIT JSON NULLS — the shape every publish request already has
    /// (no <c>WhenWritingNull</c> on the shared options, and this round does not add one).</summary>
    [TestMethod]
    public async Task Publish_NullChoices_WritesExplicitJsonNulls()
    {
        var id = Guid.NewGuid();
        var (client, handler) = Client();

        await client.PublishAsync(id, null, null, null, null);

        var body = SentBody(handler);
        body.TryGetProperty("contactIds", out var contactIds).Should().BeTrue();
        contactIds.ValueKind.Should().Be(JsonValueKind.Null);
        body.TryGetProperty("channels", out var channels).Should().BeTrue(
            "the field is present as an explicit null — the serializer options carry no ignore condition");
        channels.ValueKind.Should().Be(JsonValueKind.Null);
        body.TryGetProperty("publishEmailTemplateId", out var email).Should().BeTrue();
        email.ValueKind.Should().Be(JsonValueKind.Null);
        body.TryGetProperty("publishSmsTemplateId", out var sms).Should().BeTrue();
        sms.ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>The row-action quick publish (Index's one-arg call) keeps posting the NULL-BODY
    /// request: it reuses the assignment's stored stamps and applies the policy medium.</summary>
    [TestMethod]
    public async Task Publish_OneArgumentOverload_StillPostsANullBody()
    {
        var id = Guid.NewGuid();
        var (client, handler) = Client();

        await client.PublishAsync(id);

        handler.RequestedUris.Should().ContainSingle()
            .Which.AbsolutePath.Should().Be($"/assignments/{id}/publish");
        handler.RequestBody.Should().Be("null",
            "the quick publish sends no request object at all — null stamps reuse what is stored");
    }

    // ── GetPublishOptionsAsync ───────────────────────────────────────────────

    /// <summary>The successful read: GET the options route and deserialize the shipped DTO.</summary>
    [TestMethod]
    public async Task GetPublishOptionsAsync_ReadsTheOptionsRoute()
    {
        var id = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var (client, handler) = Client(HttpStatusCode.OK, $$"""
            {
              "channels": [
                {
                  "kind": "Publish",
                  "channel": "Email",
                  "templateId": null,
                  "subject": null,
                  "constraints": [],
                  "templates": [
                    { "templateId": "{{Guid.NewGuid()}}", "constraints": [], "isDefault": true }
                  ]
                }
              ],
              "policyPreferredChannelOrder": ["Email", "SMS"],
              "policyBlockedChannels": ["SMS"],
              "publishEmailTemplateId": "{{stamp}}",
              "publishSmsTemplateId": null
            }
            """);

        var options = await client.GetPublishOptionsAsync(id);

        handler.RequestedUris.Should().ContainSingle()
            .Which.AbsolutePath.Should().Be($"/assignments/{id}/publish-options");
        options.Should().NotBeNull();
        options!.PublishEmailTemplateId.Should().Be(stamp);
        options.Channels.Should().ContainSingle()
            .Which.Templates.Should().ContainSingle().Which.IsDefault.Should().BeTrue();
        options.PolicyBlockedChannels.Should().Equal(ContactChannelDto.SMS);
    }

    /// <summary>A non-success status (404/500) is a null, never an exception: an options outage must
    /// not block publishing. 204 is included because an empty SUCCESS body is the same outcome — the
    /// read then lands on the non-JSON class below.</summary>
    [TestMethod]
    [DataRow(HttpStatusCode.NoContent)]
    [DataRow(HttpStatusCode.NotFound)]
    [DataRow(HttpStatusCode.InternalServerError)]
    public async Task GetPublishOptionsAsync_NonSuccessStatus_ReturnsNull(HttpStatusCode status)
    {
        var (client, _) = Client(status);

        var options = await client.GetPublishOptionsAsync(Guid.NewGuid());

        options.Should().BeNull("a failed options read degrades to policy defaults");
    }

    /// <summary>A 200 whose body is not JSON is also a null — the fail-open taxonomy's non-JSON case.</summary>
    [TestMethod]
    public async Task GetPublishOptionsAsync_NonJsonSuccess_ReturnsNull()
    {
        var (client, _) = Client(HttpStatusCode.OK, "<html>gateway</html>");

        var options = await client.GetPublishOptionsAsync(Guid.NewGuid());

        options.Should().BeNull("an unparseable 200 degrades like any other failed read");
    }

    /// <summary>A THROWN transport failure (the API is unreachable) is caught: the dialog then opens
    /// with the warning bar instead of the publish action dying.</summary>
    [TestMethod]
    public async Task GetPublishOptionsAsync_ThrownTransportFailure_ReturnsNull()
    {
        var http = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://localhost") };
        var client = new AssignmentsApiClient(http, NullLogger<AssignmentsApiClient>.Instance);

        var options = await client.GetPublishOptionsAsync(Guid.NewGuid());

        options.Should().BeNull("an unreachable API must not throw out of the dialog's options read");
    }

    /// <summary>A TIMEOUT (an <see cref="OperationCanceledException"/> that is not the caller's own
    /// cancellation) is a fail-open case, not a caller teardown — the previous filter narrowed to
    /// <c>TaskCanceledException</c>, one notch below the base type a stream-read timeout can surface
    /// as, and the page's <c>catch (OperationCanceledException)</c> would then swallow it silently.</summary>
    [TestMethod]
    public async Task GetPublishOptionsAsync_Timeout_ReturnsNull()
    {
        var handler = new CapturingHandler(_ => throw new OperationCanceledException("HttpClient timeout"));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new AssignmentsApiClient(http, NullLogger<AssignmentsApiClient>.Instance);

        var options = await client.GetPublishOptionsAsync(Guid.NewGuid(), CancellationToken.None);

        options.Should().BeNull("a timeout is a fail-open case — the dialog must still open");
    }

    /// <summary>A genuine caller cancellation still propagates — the fail-open catch must not swallow
    /// the page's own teardown signal.</summary>
    [TestMethod]
    public async Task GetPublishOptionsAsync_CallerCancellation_Propagates()
    {
        var handler = new CapturingHandler(_ =>
        {
            throw new TaskCanceledException("caller gave up");
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new AssignmentsApiClient(http, NullLogger<AssignmentsApiClient>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await client.GetPublishOptionsAsync(Guid.NewGuid(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "the caller's own token cancellation is not one of the fail-open classes");
    }
}
