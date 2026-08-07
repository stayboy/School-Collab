using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Runtime client-contract tests for the topic ↔ teacher assignment surface
/// (grade-detail-rich-grids-plan.md §5): ListTopicTeachersAsync, SetTeacherTopicRoleAsync,
/// LinkTeacherTopicAsync (with role) and UnlinkTeacherTopicAsync hit the endpoints the
/// API actually exposes, using the right HTTP method.
/// </summary>
[TestClass]
public class TopicTeacherClientRoutesTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url)> Calls = new();
        private readonly Dictionary<string, string> _responses = new(StringComparer.OrdinalIgnoreCase);

        public ScriptedHandler Map(string url, string body)
        {
            _responses[url] = body;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            Calls.Add((request.Method.Method.ToUpperInvariant(), url));
            var body = _responses.TryGetValue(url, out var b) ? b : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static StudentsApiClient CreateClient(ScriptedHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var cv = new CodedValuesApiClient(new HttpClient(new ScriptedHandler()) { BaseAddress = new Uri("http://localhost") });
        return new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv);
    }

    [TestMethod]
    public async Task ListTopicTeachers_GETsTopicTeachersEndpoint()
    {
        var topicId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var handler = new ScriptedHandler().Map(
            $"/students/topics/{topicId}/teachers",
            JsonSerializer.Serialize(new[]
            {
                new { teacherId, titleCodedValueId = (Guid?)null, firstName = "Jane", lastName = "Doe",
                      displayName = (string?)null, roleCodedValueId = (Guid?)Guid.NewGuid() },
            }));
        var client = CreateClient(handler);

        var result = await client.ListTopicTeachersAsync(topicId);

        handler.Calls.Should().ContainSingle(c => c.Method == "GET" && c.Url == $"/students/topics/{topicId}/teachers");
        result.Should().ContainSingle(t => t.TeacherId == teacherId && t.FirstName == "Jane");
    }

    [TestMethod]
    public async Task SetTopicRole_PATCHesRoleEndpoint()
    {
        var teacherId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        var client = CreateClient(handler);

        await client.SetTeacherTopicRoleAsync(teacherId, topicId, roleId);

        handler.Calls.Should().ContainSingle(c =>
            c.Method == "PATCH" && c.Url == $"/teachers/{teacherId}/topics/{topicId}/role");
    }

    [TestMethod]
    public async Task LinkTeacherTopic_POSTsWithRole()
    {
        var teacherId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        var client = CreateClient(handler);

        await client.LinkTeacherTopicAsync(teacherId, topicId, roleId);

        handler.Calls.Should().ContainSingle(c =>
            c.Method == "POST" && c.Url == $"/teachers/{teacherId}/topics");
    }

    [TestMethod]
    public async Task UnlinkTeacherTopic_DELETEsTopic()
    {
        var teacherId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        var client = CreateClient(handler);

        await client.UnlinkTeacherTopicAsync(teacherId, topicId);

        handler.Calls.Should().ContainSingle(c =>
            c.Method == "DELETE" && c.Url == $"/teachers/{teacherId}/topics/{topicId}");
    }

    [TestMethod]
    public async Task ListTeacherTopicRoles_GETsTeacherTopicRolesEndpoint()
    {
        var teacherId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var handler = new ScriptedHandler().Map(
            $"/teachers/{teacherId}/topics/roles",
            JsonSerializer.Serialize(new[]
            {
                new { topicId, roleCodedValueId = (Guid?)Guid.NewGuid() },
            }));
        var client = CreateClient(handler);

        var result = await client.ListTeacherTopicRolesAsync(teacherId);

        handler.Calls.Should().ContainSingle(c =>
            c.Method == "GET" && c.Url == $"/teachers/{teacherId}/topics/roles");
        result.Should().ContainSingle(r => r.TopicId == topicId && r.RoleCodedValueId.HasValue);
    }
}
