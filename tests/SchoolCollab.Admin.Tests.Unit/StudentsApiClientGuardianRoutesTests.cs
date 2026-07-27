using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Admin.Services;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Regression tests for the guardian API route convention in
/// <see cref="StudentsApiClient"/>. Guardian ENTITY routes (CRUD,
/// name-history, students-for-guardian) live at the top-level
/// <c>/guardians</c> group (<c>StudentEndpoints.cs</c>:
/// <c>app.MapGroup("/guardians").MapGuardianRoutes()</c>) — NOT a
/// <c>/students/guardians</c> sub-resource. Only the student↔guardian
/// LINK relationship nests under <c>/students/{studentId}/guardians</c>.
/// <para>
/// These tests assert the exact request URI each client method emits.
/// They would have FAILED before the fix that changed the entity routes
/// from <c>/students/guardians</c> to <c>/guardians</c> — the bug surfaced
/// as a 405 when adding a guardian on student edit
/// (<c>CreateGuardianAsync</c> POST <c>/students/guardians</c> hit no
/// route). The same class of bug previously affected contacts (see the
/// comment in <c>StudentsApiClient.cs</c> ~line 754); these tests guard the
/// guardian side against regressing back to the nested prefix.
/// </para>
/// </summary>
[TestClass]
public class StudentsApiClientGuardianRoutesTests
{
    /// <summary>
    /// CreateGuardianAsync must POST the guardian ENTITY to the top-level
    /// <c>/guardians</c> route (the <c>MapGroup("/guardians")</c> group), NOT
    /// <c>/students/guardians</c>. Before the fix this posted to
    /// <c>/students/guardians</c> and the API returned 405 (no such route),
    /// breaking "add guardian on student edit".
    /// </summary>
    [TestMethod]
    public async Task CreateGuardianAsync_PostsToTopLevelGuardiansRoute_NotStudentsGuardians()
    {
        var createdId = Guid.NewGuid();
        var handler = new CapturingHandler(JsonResponse(createdId));
        var api = NewApiClient(handler);

        var result = await api.CreateGuardianAsync(
            new CreateGuardianRequest(null, "Ada", "Lovestrace", null, null, null), default);

        result.Should().Be(createdId);
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/guardians",
            "guardian entity CRUD is the top-level /guardians group, not /students/guardians");
    }

    /// <summary>
    /// ListGuardiansAsync must GET the top-level <c>/guardians</c> route
    /// (with the optional <c>?search=</c> query). The picker grid and the
    /// Guardians admin page both rely on this; a wrong prefix returns 404
    /// and the grid silently renders empty.
    /// </summary>
    [TestMethod]
    public async Task ListGuardiansAsync_GetsTopLevelGuardiansRoute()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Array.Empty<object>())
        });
        var api = NewApiClient(handler);

        await api.ListGuardiansAsync(default, search: "Ada");

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/guardians");
        handler.LastRequest.RequestUri.Query.Should().Contain("search=Ada");
    }

    /// <summary>
    /// LinkGuardianAsync must POST the student↔guardian LINK to the nested
    /// <c>/students/{studentId}/guardians</c> route (a relationship under the
    /// student — the only guardian route that lives under /students). This
    /// guards against an over-correction that moves the link route to
    /// /guardians too.
    /// </summary>
    [TestMethod]
    public async Task LinkGuardianAsync_PostsToNestedStudentGuardiansRoute()
    {
        var studentId = Guid.NewGuid();
        var guardianId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var handler = new CapturingHandler(JsonResponse(linkId));
        var api = NewApiClient(handler);

        var result = await api.LinkGuardianAsync(
            new LinkGuardianRequest(studentId, guardianId, null, GuardianRole.Primary, false, null),
            default);

        result.Should().Be(linkId);
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be($"/students/{studentId}/guardians",
            "the student↔guardian link is a relationship nested under the student");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static StudentsApiClient NewApiClient(CapturingHandler handler)
    {
        // CreateGuardianAsync / ListGuardiansAsync / LinkGuardianAsync never call
        // the coded-values client, so its HttpClient can be any stub — it exists
        // only to satisfy the StudentsApiClient constructor.
        var codedValues = new CodedValuesApiClient(new HttpClient(new CapturingHandler(
            new HttpResponseMessage(HttpStatusCode.NotFound))));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        return new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues);
    }

    private static HttpResponseMessage JsonResponse(Guid id) => new(HttpStatusCode.Created)
    {
        // CreateGuardianAsync / LinkGuardianAsync read { "id": "..." } via the
        // private IdResponse record. EnsureSuccessStatusCode passes for 201.
        Content = new StringContent($"{{\"id\":\"{id}\"}}", Encoding.UTF8, "application/json")
    };

    /// <summary>
    /// A minimal <see cref="HttpMessageHandler"/> that records the last
    /// <see cref="HttpRequestMessage"/> sent through it and returns a canned
    /// <see cref="HttpResponseMessage"/>. No real network call is made.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public HttpRequestMessage? LastRequest { get; private set; }

        public CapturingHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }
}