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
/// Integration tests for <c>GET /students/by-grade/{gradeLevelId}?periodId=</c>
/// against real Postgres (via <see cref="ApiFactory"/>). Pins the SQL translation
/// of <c>ListStudentsByGradeHandler</c>'s join + ordering. The handler previously
/// applied <c>OrderBy/ThenBy</c> AFTER projecting into the custom <c>StudentDto</c>
/// type, which the relational provider cannot translate ("could not be translated"
/// → <c>InvalidOperationException</c> at runtime) even though the InMemory unit
/// tests pass. Ordering is now done on an anonymous projection before the final
/// DTO projection; this test guards that regression.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ListStudentsByGradeEndpointTests
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
    public async Task ByGrade_ReturnsActiveStudents_OrderedByName()
    {
        var tenantId = Guid.NewGuid();
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        var periodId = await SeedCurrentPeriodAsync(tenantId, "Term 1");

        await SeedAsync(tenantId, async db =>
        {
            db.Students.Add(Student.Create("S1", "Anna", "Smith", new DateOnly(2015, 1, 1), Guid.NewGuid()));
            db.Students.Add(Student.Create("S2", "Bob", "Jones", new DateOnly(2015, 2, 1), Guid.NewGuid()));
            await db.SaveChangesAsync();
            var s1 = await db.Students.SingleAsync(x => x.StudentNumber == "S1");
            var s2 = await db.Students.SingleAsync(x => x.StudentNumber == "S2");
            db.StudentEnrollments.Add(StudentEnrollment.Create(s1.Id, periodId, gradeLevelId));
            db.StudentEnrollments.Add(StudentEnrollment.Create(s2.Id, periodId, gradeLevelId));
            await db.SaveChangesAsync();
            return true;
        });

        var response = await SendAsync(
            HttpMethod.Get, $"/students/by-grade/{gradeLevelId}?periodId={periodId}", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the join + ordering must translate on Postgres, not throw InvalidOperationException");
        var body = await response.Content.ReadFromJsonAsync<StudentDto[]>();
        body.Should().NotBeNull();
        body!.Select(x => x.StudentNumber).Should().BeEquivalentTo(new[] { "S2", "S1" });
        body.Select(x => x.LastName).Should().BeInAscendingOrder();
    }

    [TestMethod]
    public async Task ByGrade_NoPeriod_ReturnsEmpty()
    {
        var tenantId = Guid.NewGuid();
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");

        var response = await SendAsync(
            HttpMethod.Get, $"/students/by-grade/{gradeLevelId}", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StudentDto[]>();
        body.Should().NotBeNull();
        body!.Should().BeEmpty();
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
            var period = Period.Create(name, today.AddDays(-1), today.AddDays(1), AcademicYearDivision.None);
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
