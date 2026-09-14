using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-C1 (spec §5 / §7 Q5) coverage for the HTTP-backed
/// <see cref="StudentDirectoryHttpClient"/>: ward-name + guardian-link reads
/// from the students-api, with the best-effort degradation posture (404/network
/// ⇒ null/empty) and the fail-CLOSED <c>IsGuardianOfAsync</c> (any failure ⇒
/// false — the sign handler must never authenticate an unlinked guardian).
/// </summary>
[TestClass]
public class StudentDirectoryHttpClientTests
{
    private static readonly Guid StudentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GuardianId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherGuardianId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static StudentDirectoryHttpClient NewClient(MockHttpMessageHandler studentsApi)
    {
        return new StudentDirectoryHttpClient(
            new StubStudentsHttpClientFactory(studentsApi),
            NullLogger<StudentDirectoryHttpClient>.Instance);
    }

    // ── GetStudentNameAsync ───────────────────────────────────────────────

    [TestMethod]
    public async Task GetStudentName_WithStudent_MapsFirstLast()
    {
        var students = new MockHttpMessageHandler();
        students.When($"http://students-api/students/{StudentId}")
            .Respond("application/json", JsonSerializer.Serialize(
                new StudentDto(
                    StudentId, "S1001", null, "Jane", "Doe", null, null, false,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), JsonOptions));

        var result = await NewClient(students).GetStudentNameAsync(StudentId);

        result.Should().NotBeNull();
        result!.DisplayName.Should().Be("Jane Doe");
    }

    [TestMethod]
    public async Task GetStudentName_NotFound_ReturnsNull()
    {
        var students = new MockHttpMessageHandler();
        students.When($"http://students-api/students/{StudentId}")
            .Respond(HttpStatusCode.NotFound);

        var result = await NewClient(students).GetStudentNameAsync(StudentId);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task GetStudentName_NetworkError_ReturnsNull()
    {
        var students = new MockHttpMessageHandler();
        students.When($"http://students-api/students/{StudentId}")
            .Respond(HttpStatusCode.BadGateway);

        var result = await NewClient(students).GetStudentNameAsync(StudentId);

        result.Should().BeNull();
    }

    // ── GetGuardiansAsync ─────────────────────────────────────────────────

    private static void RespondGuardians(MockHttpMessageHandler students)
    {
        StudentGuardianViewDto[] guardians =
        [
            new StudentGuardianViewDto(
                GuardianId, StudentId, GuardianRole.Primary, null, false,
                "Jane", "Doe", null),
            new StudentGuardianViewDto(
                OtherGuardianId, StudentId, GuardianRole.CC, null, false,
                "John", "Smith", "Johnny Smith")
        ];

        students.When($"http://students-api/students/{StudentId}/guardians")
            .Respond("application/json", JsonSerializer.Serialize(guardians, JsonOptions));
    }

    [TestMethod]
    public async Task GetGuardians_MapsLinks_IncludingPrimaryAndDisplayName()
    {
        var students = new MockHttpMessageHandler();
        RespondGuardians(students);

        var result = await NewClient(students).GetGuardiansAsync(StudentId);

        result.Should().HaveCount(2);
        result[0].GuardianId.Should().Be(GuardianId);
        result[0].IsPrimary.Should().BeTrue();
        result[0].DisplayName.Should().Be("Jane Doe");
        result[1].IsPrimary.Should().BeFalse();
        result[1].DisplayName.Should().Be("Johnny Smith");
    }

    [TestMethod]
    public async Task GetGuardians_NetworkError_ReturnsEmpty()
    {
        var students = new MockHttpMessageHandler();
        students.When($"http://students-api/students/{StudentId}/guardians")
            .Respond(HttpStatusCode.BadGateway);

        var result = await NewClient(students).GetGuardiansAsync(StudentId);

        result.Should().BeEmpty();
    }

    // ── IsGuardianOfAsync ─────────────────────────────────────────────────

    [TestMethod]
    public async Task IsGuardianOf_LinkedGuardian_ReturnsTrue()
    {
        var students = new MockHttpMessageHandler();
        RespondGuardians(students);

        var result = await NewClient(students).IsGuardianOfAsync(StudentId, GuardianId);

        result.Should().BeTrue();
    }

    [TestMethod]
    public async Task IsGuardianOf_UnlinkedGuardian_ReturnsFalse()
    {
        var students = new MockHttpMessageHandler();
        RespondGuardians(students);

        var result = await NewClient(students).IsGuardianOfAsync(
            StudentId, Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));

        result.Should().BeFalse();
    }

    [TestMethod]
    public async Task IsGuardianOf_NetworkError_FailsClosed()
    {
        var students = new MockHttpMessageHandler();
        students.When($"http://students-api/students/{StudentId}/guardians")
            .Respond(HttpStatusCode.BadGateway);

        var result = await NewClient(students).IsGuardianOfAsync(StudentId, GuardianId);

        result.Should().BeFalse();
    }

    private sealed class StubStudentsHttpClientFactory(MockHttpMessageHandler studentsApi) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = studentsApi.ToHttpClient();
            client.BaseAddress = new Uri("http://students-api");
            return client;
        }
    }
}