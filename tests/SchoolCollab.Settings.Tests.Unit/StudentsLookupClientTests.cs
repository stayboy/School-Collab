using System.Net;
using System.Text;
using FluentAssertions;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Wire contract of <see cref="StudentsLookupClient"/> (round notification-templates-b3b). Its three
/// <see cref="LookupOptionDto"/> shapes are local MIRRORS of Students payloads — the same drift class
/// as the repo's enum mirrors. These tests pin the paths and the two read fields, so mirror drift
/// surfaces as a failing test instead of a constraint value that silently never matches at publish.
/// </summary>
[TestClass]
public class StudentsLookupClientTests
{
    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            var response = new HttpResponseMessage(status);
            if (status != HttpStatusCode.NoContent)
            {
                response.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }

    private static (StudentsLookupClient Client, RecordingHandler Handler) Build(
        HttpStatusCode status = HttpStatusCode.OK, string body = "[]")
    {
        var handler = new RecordingHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        return (new StudentsLookupClient(http), handler);
    }

    [TestMethod]
    public async Task ListGradeLevelsAsync_HitsTheStudentsGradeLevelPath_AndReadsIdAndName()
    {
        var (client, handler) = Build(body: """
            [{ "id": "11111111-1111-1111-1111-111111111111", "name": "Grade 8", "level": 8, "codedValueId": "22222222-2222-2222-2222-222222222222" }]
            """);

        var options = await client.ListGradeLevelsAsync();

        handler.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/students/grade-levels");
        options.Should().ContainSingle();
        options[0].Id.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        options[0].Name.Should().Be("Grade 8");
    }

    [TestMethod]
    public async Task ListTopicsAsync_HitsTheStudentsTopicsPath()
    {
        var (client, handler) = Build(body: """
            [{ "id": "33333333-3333-3333-3333-333333333333", "name": "Mathematics" }]
            """);

        var options = await client.ListTopicsAsync();

        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/students/topics");
        options.Should().ContainSingle().Which.Name.Should().Be("Mathematics");
    }

    [TestMethod]
    public async Task ListActivityGroupsAsync_HitsTheActivityGroupsPath_AndCarriesTheActiveFlag()
    {
        var (client, handler) = Build(body: """
            [
              { "id": "44444444-4444-4444-4444-444444444444", "name": "Chess club", "isActive": true },
              { "id": "55555555-5555-5555-5555-555555555555", "name": "Archived club", "isActive": false }
            ]
            """);

        var options = await client.ListActivityGroupsAsync();

        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/activity-groups");
        options.Should().HaveCount(2);
        options[0].IsActive.Should().BeTrue();
        options[1].IsActive.Should().BeFalse("the editor never offers an archived group — it could never match");
    }

    [TestMethod]
    public async Task EveryLookup_ThrowsOnANonSuccessResponse()
    {
        var (client, _) = Build(HttpStatusCode.InternalServerError, "boom");

        await FluentActions.Awaiting(() => client.ListGradeLevelsAsync()).Should().ThrowAsync<HttpRequestException>();
        await FluentActions.Awaiting(() => client.ListTopicsAsync()).Should().ThrowAsync<HttpRequestException>();
        await FluentActions.Awaiting(() => client.ListActivityGroupsAsync()).Should().ThrowAsync<HttpRequestException>();
    }

    /// <summary>
    /// A route this host does not map answers 404, and that is reported as null — not thrown. It is
    /// the live case for activity groups while <c>FEATURE:EnableActivityGroups</c> is off, and it is
    /// a feature posture rather than a fault, so the caller must be able to tell it from an outage.
    /// </summary>
    [TestMethod]
    public async Task EveryLookup_ReturnsNullWhenTheRouteIsNotMapped()
    {
        var (client, _) = Build(HttpStatusCode.NotFound, "no endpoint matched");

        (await client.ListGradeLevelsAsync()).Should().BeNull();
        (await client.ListTopicsAsync()).Should().BeNull();
        (await client.ListActivityGroupsAsync()).Should().BeNull(
            "an unmapped /activity-groups is the flag-off posture, and null is how the caller tells "
            + "it apart from a 500");
    }
}
