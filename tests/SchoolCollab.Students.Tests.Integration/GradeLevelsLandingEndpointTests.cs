using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// Integration tests for the <c>GET /students/grade-levels/landing</c> endpoint
/// against real Postgres (via <see cref="ApiFactory"/>). Pins the SQL translation
/// of the StrandCount / LessonCount projections added to
/// <c>ListGradeLevelsForLandingHandler</c> — the in-memory unit tests do not
/// exercise provider translation, so a correlated <c>SelectMany</c> in a
/// projection can pass unit tests yet throw at runtime ("could not be
/// translated"), which surfaces in the UI as an infinite spinner.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GradeLevelsLandingEndpointTests
{
    private static ApiFactory _factory = default!;
    private static HttpClient _client = default!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _factory = new ApiFactory();
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        // Tables (note legacy renames): Topic→subjects, TopicStrand→subject_strands,
        // TopicLesson→subject_lessons; GradeTopicAssignment is TPH under topic_assignments.
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE subject_strands, subject_lessons, topic_assignments, student_topic_assignments, grade_levels, subjects, periods CASCADE;");
    }

    /// <summary>
    /// Seeds a grade + current period + effective topic assignment + strands +
    /// lessons and asserts the landing endpoint returns 200 with the correct
    /// Topic/Strand/Lesson/Student counts. If the new SelectMany projections
    /// fail to translate on Npgsql, this test fails with the server error.
    /// </summary>
    [TestMethod]
    public async Task Landing_WithStrandsAndLessons_Returns200WithCounts()
    {
        // Unique tenant per test so the HybridCache key never collides across tests.
        var tenantId = Guid.NewGuid();

        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        await SeedCurrentPeriodAsync(tenantId, "Term 1");

        var topicA = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        var topicB = Topic.Create(Guid.NewGuid(), "SCI", "Science", 2);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedAsync(tenantId, async db =>
        {
            db.Topics.Add(topicA);
            db.Topics.Add(topicB);
            db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(gradeLevelId, topicA.Id, today));
            db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(gradeLevelId, topicB.Id, today));

            // 3 strands on topic A, 1 on topic B → StrandCount 4.
            db.TopicStrands.Add(TopicStrand.Create(topicA.Id, "Algebra", null, 1));
            db.TopicStrands.Add(TopicStrand.Create(topicA.Id, "Geometry", null, 2));
            db.TopicStrands.Add(TopicStrand.Create(topicA.Id, "Statistics", null, 3));
            db.TopicStrands.Add(TopicStrand.Create(topicB.Id, "Biology", null, 1));

            // 5 lessons on topic A, 2 on topic B → LessonCount 7.
            db.TopicLessons.Add(TopicLesson.Create(topicA.Id, "L1", null, null, null, 1));
            db.TopicLessons.Add(TopicLesson.Create(topicA.Id, "L2", null, null, null, 2));
            db.TopicLessons.Add(TopicLesson.Create(topicA.Id, "L3", null, null, null, 3));
            db.TopicLessons.Add(TopicLesson.Create(topicA.Id, "L4", null, null, null, 4));
            db.TopicLessons.Add(TopicLesson.Create(topicA.Id, "L5", null, null, null, 5));
            db.TopicLessons.Add(TopicLesson.Create(topicB.Id, "A1", null, null, null, 1));
            db.TopicLessons.Add(TopicLesson.Create(topicB.Id, "A2", null, null, null, 2));
            await db.SaveChangesAsync();
            return true;
        });

        var response = await SendAsync(HttpMethod.Get, "/students/grade-levels/landing", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the landing query (incl. the new SelectMany StrandCount/LessonCount projections) must translate on Postgres, not 500");
        var body = await response.Content.ReadFromJsonAsync<GradeLevelLandingDto[]>();
        body.Should().NotBeNull();
        body.Should().ContainSingle();
        var row = body![0];
        row.TopicCount.Should().Be(2);
        row.StrandCount.Should().Be(4);
        row.LessonCount.Should().Be(7);
        row.StudentCount.Should().Be(0, "no students enrolled");
    }

    /// <summary>
    /// No current period → StudentCount 0, but Strand/Lesson counts are still
    /// computed (they are date-effective, not period-bound).
    /// </summary>
    [TestMethod]
    public async Task Landing_NoCurrentPeriod_StillComputesStrandAndLessonCounts()
    {
        var tenantId = Guid.NewGuid();
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");

        var topic = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        await SeedAsync(tenantId, async db =>
        {
            db.Topics.Add(topic);
            db.GradeTopicAssignments.Add(
                GradeTopicAssignment.Create(gradeLevelId, topic.Id, DateOnly.FromDateTime(DateTime.UtcNow)));
            db.TopicStrands.Add(TopicStrand.Create(topic.Id, "Algebra", null, 1));
            db.TopicLessons.Add(TopicLesson.Create(topic.Id, "Lesson 1", null, null, null, 1));
            await db.SaveChangesAsync();
            return true;
        });

        var response = await SendAsync(HttpMethod.Get, "/students/grade-levels/landing", tenantId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GradeLevelLandingDto[]>();
        body.Should().NotBeNull();
        body.Should().ContainSingle();
        var row = body![0];
        row.StrandCount.Should().Be(1);
        row.LessonCount.Should().Be(1);
        row.StudentCount.Should().Be(0);
    }

    // ── seed helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> SeedGradeLevelAsync(Guid tenantId, string name)
        => await SeedAsync(tenantId, async db =>
        {
            var gl = GradeLevel.Create(Guid.NewGuid(), 1, name, 1);
            db.GradeLevels.Add(gl);
            await db.SaveChangesAsync();
            return gl.Id;
        });

    private async Task SeedCurrentPeriodAsync(Guid tenantId, string name)
        => await SeedAsync(tenantId, async db =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var period = Period.Create(name, today.AddDays(-1), today.AddDays(1));
            db.Periods.Add(period);
            await db.SaveChangesAsync();
            return true;
        });

    private async Task<T> SeedAsync<T>(Guid tenantId, Func<StudentsDbContext, Task<T>> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ => await seed(db));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Guid tenantId)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        return _client.SendAsync(request);
    }
}