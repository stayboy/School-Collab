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
/// Integration tests for the bulk student-count endpoint, against real Postgres
/// (via <see cref="ApiFactory"/>).
///
/// <c>GET /guardians/student-counts?guardianIds=…</c> backs the guardians landing
/// page's "N students" cell (the reverse of the student landing page's "N guardians"
/// cell). It must count linked NON-deleted students per guardian in one round-trip
/// and be tenant-scoped.
/// </summary>
[TestClass]
[DoNotParallelize]
public class StudentCountEndpointTests
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
            "TRUNCATE TABLE student_guardians, guardians, students CASCADE;");
    }

    [TestMethod]
    public async Task StudentCounts_ReturnsCount_OfLinkedNonDeletedStudents()
    {
        var tenantId = Guid.NewGuid();

        var (guardianId, s1, s2, sDeleted) = await SeedAsync(tenantId, async db =>
        {
            var guardian = Guardian.Create(null, "Parent", "Count", null, null, null);
            db.Guardians.Add(guardian);
            var student1 = Student.Create("4001", "Maya", "Lin", new DateOnly(2014, 5, 1), Guid.NewGuid());
            var student2 = Student.Create("4002", "Nora", "Jones", new DateOnly(2015, 1, 1), Guid.NewGuid());
            var studentDeleted = Student.Create("4003", "Gone", "Away", new DateOnly(2015, 2, 2), Guid.NewGuid());
            db.Students.Add(student1);
            db.Students.Add(student2);
            db.Students.Add(studentDeleted);
            await db.SaveChangesAsync();

            var g = await db.Guardians.SingleAsync(x => x.LastName == "Count");
            var st1 = await db.Students.SingleAsync(x => x.StudentNumber == "4001");
            var st2 = await db.Students.SingleAsync(x => x.StudentNumber == "4002");
            var stDel = await db.Students.SingleAsync(x => x.StudentNumber == "4003");

            db.StudentGuardians.Add(StudentGuardian.Create(st1.Id, g.Id, GuardianRole.Primary, null, false, null));
            db.StudentGuardians.Add(StudentGuardian.Create(st2.Id, g.Id, GuardianRole.Primary, null, false, null));
            db.StudentGuardians.Add(StudentGuardian.Create(stDel.Id, g.Id, GuardianRole.Primary, null, false, null));
            await db.SaveChangesAsync();

            // Soft-delete one linked student — it must NOT count.
            stDel.Delete();
            await db.SaveChangesAsync();

            return (g.Id, st1.Id, st2.Id, stDel.Id);
        });

        var query = $"guardianIds={guardianId}";
        var response = await SendAsync(HttpMethod.Get, $"/guardians/student-counts?{query}", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StudentCountDto[]>();
        body.Should().NotBeNull();
        var row = body!.Single();
        row.GuardianId.Should().Be(guardianId);
        row.Count.Should().Be(2, "the soft-deleted student is excluded from the count");
    }

    [TestMethod]
    public async Task StudentCounts_EmptyInput_ReturnsEmptyArray()
    {
        var tenantId = Guid.NewGuid();

        var response = await SendAsync(
            HttpMethod.Get, "/guardians/student-counts", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StudentCountDto[]>();
        body.Should().NotBeNull();
        body!.Should().BeEmpty();
    }

    [TestMethod]
    public async Task StudentCounts_DoesNotLeakAnotherTenantsRows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var guardianId = await SeedAsync(tenantA, async db =>
        {
            var guardian = Guardian.Create(null, "Tenant", "Only", null, null, null);
            db.Guardians.Add(guardian);
            var student = Student.Create("4004", "Tenant", "One", new DateOnly(2014, 5, 1), Guid.NewGuid());
            db.Students.Add(student);
            await db.SaveChangesAsync();

            var g = await db.Guardians.SingleAsync(x => x.LastName == "Only");
            var s = await db.Students.SingleAsync(x => x.StudentNumber == "4004");
            db.StudentGuardians.Add(StudentGuardian.Create(s.Id, g.Id, GuardianRole.Primary, null, false, null));
            await db.SaveChangesAsync();
            return g.Id;
        });

        var query = $"guardianIds={guardianId}";
        var response = await SendAsync(
            HttpMethod.Get, $"/guardians/student-counts?{query}", tenantB);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StudentCountDto[]>();
        body.Should().NotBeNull();
        body!.Should().BeEmpty("student counts are tenant-scoped via the explicit tenant filter");
    }

    [TestMethod]
    public async Task StudentCounts_MultipleGuardians_ReturnsRowsForEach()
    {
        var tenantId = Guid.NewGuid();

        var ids = await SeedAsync(tenantId, async db =>
        {
            var g1 = Guardian.Create(null, "Parent", "Alpha", null, null, null);
            var g2 = Guardian.Create(null, "Parent", "Beta", null, null, null);
            db.Guardians.Add(g1);
            db.Guardians.Add(g2);
            var student = Student.Create("4005", "Shared", "Student", new DateOnly(2014, 5, 1), Guid.NewGuid());
            db.Students.Add(student);
            await db.SaveChangesAsync();

            var a = await db.Guardians.SingleAsync(x => x.LastName == "Alpha");
            var b = await db.Guardians.SingleAsync(x => x.LastName == "Beta");
            var s = await db.Students.SingleAsync(x => x.StudentNumber == "4005");
            db.StudentGuardians.Add(StudentGuardian.Create(s.Id, a.Id, GuardianRole.Primary, null, false, null));
            db.StudentGuardians.Add(StudentGuardian.Create(s.Id, b.Id, GuardianRole.CC, null, false, null));
            await db.SaveChangesAsync();
            return (a.Id, b.Id);
        });

        var query = $"guardianIds={ids.Item1}&guardianIds={ids.Item2}";
        var response = await SendAsync(HttpMethod.Get, $"/guardians/student-counts?{query}", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StudentCountDto[]>();
        body.Should().NotBeNull();
        body!.Should().HaveCount(2, "a row is returned per requested guardian with at least one linked student");
        body!.Select(x => x.GuardianId).Should().BeEquivalentTo(new[] { ids.Item1, ids.Item2 });
        body!.All(x => x.Count == 1).Should().BeTrue("both guardians are linked to the same single student");
    }

    // ── seed helpers ──────────────────────────────────────────────────────────

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
