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
/// Integration tests for <c>GET /students/enrollments/by-students?studentIds=</c>
/// against real Postgres (via <see cref="ApiFactory"/>). The bulk endpoint backs
/// the client-side <c>EnrichStudentsAsync</c> grade hydration; it must return
/// every enrollment for all requested students in a single round-trip and be
/// tenant-scoped (a student id belonging to another tenant must never leak rows).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ListEnrollmentsByStudentsEndpointTests
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
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE student_enrollments, students, grade_levels, periods CASCADE;");
    }

    [TestMethod]
    public async Task ByStudents_ReturnsEnrollments_ForAllRequestedStudents()
    {
        var tenantId = Guid.NewGuid();
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 2");
        var periodId = await SeedCurrentPeriodAsync(tenantId, "Term 1");

        var (s1, s2) = await SeedAsync(tenantId, async db =>
        {
            db.Students.Add(Student.Create("S1", "Anna", "Smith", new DateOnly(2015, 1, 1), Guid.NewGuid()));
            db.Students.Add(Student.Create("S2", "Bob", "Jones", new DateOnly(2015, 2, 1), Guid.NewGuid()));
            await db.SaveChangesAsync();
            var a = await db.Students.SingleAsync(x => x.StudentNumber == "S1");
            var b = await db.Students.SingleAsync(x => x.StudentNumber == "S2");
            db.StudentEnrollments.Add(StudentEnrollment.Create(a.Id, periodId, gradeLevelId));
            db.StudentEnrollments.Add(StudentEnrollment.Create(b.Id, periodId, gradeLevelId));
            await db.SaveChangesAsync();
            return (a.Id, b.Id);
        });

        var query = string.Join("&", new[] { s1, s2 }.Select(id => $"studentIds={id}"));
        var response = await SendAsync(HttpMethod.Get, $"/students/enrollments/by-students?{query}", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StudentEnrollmentDto[]>();
        body.Should().NotBeNull();
        body!.Select(x => x.StudentId).Should().BeEquivalentTo(new[] { s1, s2 });
        body.Should().OnlyContain(x => x.Status == "Active");
    }

    [TestMethod]
    public async Task ByStudents_EmptyInput_ReturnsEmptyArray()
    {
        var tenantId = Guid.NewGuid();

        var response = await SendAsync(
            HttpMethod.Get, "/students/enrollments/by-students", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StudentEnrollmentDto[]>();
        body.Should().NotBeNull();
        body!.Should().BeEmpty();
    }

    [TestMethod]
    public async Task ByStudents_DoesNotLeakAnotherTenantsRows()
    {
        // Student belongs to tenant A; the bulk query runs under tenant B, so it
        // must not return the row even when asked for that exact student id.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var gradeLevelId = await SeedGradeLevelAsync(tenantA, "Grade 3");
        var periodId = await SeedCurrentPeriodAsync(tenantA, "Term 1");

        var studentId = await SeedAsync(tenantA, async db =>
        {
            db.Students.Add(Student.Create("S1", "Anna", "Smith", new DateOnly(2015, 1, 1), Guid.NewGuid()));
            await db.SaveChangesAsync();
            var s = await db.Students.SingleAsync(x => x.StudentNumber == "S1");
            db.StudentEnrollments.Add(StudentEnrollment.Create(s.Id, periodId, gradeLevelId));
            await db.SaveChangesAsync();
            return s.Id;
        });

        var query = $"studentIds={studentId}";
        var response = await SendAsync(
            HttpMethod.Get, $"/students/enrollments/by-students?{query}", tenantB);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StudentEnrollmentDto[]>();
        body.Should().NotBeNull();
        body!.Should().BeEmpty("enrollment rows are tenant-scoped via the explicit tenant filter");
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

    private async Task<Guid> SeedCurrentPeriodAsync(Guid tenantId, string name)
        => await SeedAsync(tenantId, async db =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var period = Period.Create(name, today.AddDays(-1), today.AddDays(1));
            db.Periods.Add(period);
            await db.SaveChangesAsync();
            return period.Id;
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
