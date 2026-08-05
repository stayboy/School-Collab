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
/// Integration tests for the guardian-count bulk endpoint and the student-scoped
/// guardians filter, against real Postgres (via <see cref="ApiFactory"/>).
///
/// <c>GET /students/guardian-counts?studentIds=…</c> backs the student landing
/// page's "N guardians" cell; it must count linked NON-deleted guardians per
/// student in one round-trip and be tenant-scoped. The <c>GET /guardians?studentId=…</c>
/// filter backs the student-scoped guardians view reached from that cell.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GuardianCountAndScopedListEndpointTests
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
    public async Task GuardianCounts_ReturnsCount_OfLinkedNonDeletedGuardians()
    {
        var tenantId = Guid.NewGuid();

        var (studentId, g1, g2, gDeleted) = await SeedAsync(tenantId, async db =>
        {
            var student = Student.Create("3001", "Maya", "Lin", new DateOnly(2014, 5, 1), Guid.NewGuid());
            db.Students.Add(student);
            var guardian1 = Guardian.Create(null, "Parent", "One", null, null, null);
            var guardian2 = Guardian.Create(null, "Parent", "Two", null, null, null);
            var guardianDeleted = Guardian.Create(null, "Parent", "Gone", null, null, null);
            db.Guardians.Add(guardian1);
            db.Guardians.Add(guardian2);
            db.Guardians.Add(guardianDeleted);
            await db.SaveChangesAsync();

            var s = await db.Students.SingleAsync(x => x.StudentNumber == "3001");
            var gA = await db.Guardians.SingleAsync(x => x.LastName == "One");
            var gB = await db.Guardians.SingleAsync(x => x.LastName == "Two");
            var gDel = await db.Guardians.SingleAsync(x => x.LastName == "Gone");

            db.StudentGuardians.Add(StudentGuardian.Create(s.Id, gA.Id, GuardianRole.Primary, null, false, null));
            db.StudentGuardians.Add(StudentGuardian.Create(s.Id, gB.Id, GuardianRole.Primary, null, false, null));
            db.StudentGuardians.Add(StudentGuardian.Create(s.Id, gDel.Id, GuardianRole.Primary, null, false, null));
            await db.SaveChangesAsync();

            // Soft-delete one linked guardian — it must NOT count.
            gDel.SoftDelete();
            await db.SaveChangesAsync();

            return (s.Id, gA.Id, gB.Id, gDel.Id);
        });

        var query = $"studentIds={studentId}";
        var response = await SendAsync(HttpMethod.Get, $"/students/guardian-counts?{query}", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GuardianCountDto[]>();
        body.Should().NotBeNull();
        var row = body!.Single();
        row.StudentId.Should().Be(studentId);
        row.Count.Should().Be(2, "the soft-deleted guardian is excluded from the count");
    }

    [TestMethod]
    public async Task GuardianCounts_EmptyInput_ReturnsEmptyArray()
    {
        var tenantId = Guid.NewGuid();

        var response = await SendAsync(
            HttpMethod.Get, "/students/guardian-counts", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GuardianCountDto[]>();
        body.Should().NotBeNull();
        body!.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GuardianCounts_DoesNotLeakAnotherTenantsRows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var studentId = await SeedAsync(tenantA, async db =>
        {
            var student = Student.Create("3002", "Tenant", "One", new DateOnly(2014, 5, 1), Guid.NewGuid());
            db.Students.Add(student);
            var guardian = Guardian.Create(null, "Parent", "Only", null, null, null);
            db.Guardians.Add(guardian);
            await db.SaveChangesAsync();

            var s = await db.Students.SingleAsync(x => x.StudentNumber == "3002");
            var g = await db.Guardians.SingleAsync(x => x.LastName == "Only");
            db.StudentGuardians.Add(StudentGuardian.Create(s.Id, g.Id, GuardianRole.Primary, null, false, null));
            await db.SaveChangesAsync();
            return s.Id;
        });

        var query = $"studentIds={studentId}";
        var response = await SendAsync(
            HttpMethod.Get, $"/students/guardian-counts?{query}", tenantB);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GuardianCountDto[]>();
        body.Should().NotBeNull();
        body!.Should().BeEmpty("guardian counts are tenant-scoped via the explicit tenant filter");
    }

    [TestMethod]
    public async Task ScopedList_ReturnsOnlyGuardiansLinkedToStudent()
    {
        var tenantId = Guid.NewGuid();

        var (studentId, linkedGuardianId, otherGuardianId) = await SeedAsync(tenantId, async db =>
        {
            var student = Student.Create("3003", "Scoped", "Student", new DateOnly(2014, 5, 1), Guid.NewGuid());
            db.Students.Add(student);
            var linked = Guardian.Create(null, "Linked", "Guardian", null, null, null);
            var other = Guardian.Create(null, "Other", "Guardian", null, null, null);
            db.Guardians.Add(linked);
            db.Guardians.Add(other);
            await db.SaveChangesAsync();

            var s = await db.Students.SingleAsync(x => x.StudentNumber == "3003");
            var l = await db.Guardians.SingleAsync(x => x.FirstName == "Linked");
            var o = await db.Guardians.SingleAsync(x => x.FirstName == "Other");
            db.StudentGuardians.Add(StudentGuardian.Create(s.Id, l.Id, GuardianRole.Primary, null, false, null));
            await db.SaveChangesAsync();
            return (s.Id, l.Id, o.Id);
        });

        var response = await SendAsync(
            HttpMethod.Get, $"/guardians?studentId={studentId}", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GuardianDto[]>();
        body.Should().NotBeNull();
        body!.Select(x => x.Id).Should().BeEquivalentTo(new[] { linkedGuardianId },
            "only guardians linked to the requested student are returned");
        body!.Select(x => x.Id).Should().NotContain(otherGuardianId);
    }

    [TestMethod]
    public async Task ScopedList_Unfiltered_ReturnsAllGuardians()
    {
        var tenantId = Guid.NewGuid();

        var ids = await SeedAsync(tenantId, async db =>
        {
            var a = Guardian.Create(null, "Alpha", "One", null, null, null);
            var b = Guardian.Create(null, "Beta", "Two", null, null, null);
            db.Guardians.Add(a);
            db.Guardians.Add(b);
            await db.SaveChangesAsync();
            return new[]
            {
                (await db.Guardians.SingleAsync(x => x.FirstName == "Alpha")).Id,
                (await db.Guardians.SingleAsync(x => x.FirstName == "Beta")).Id,
            };
        });

        var response = await SendAsync(HttpMethod.Get, "/guardians", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GuardianDto[]>();
        body.Should().NotBeNull();
        body!.Select(x => x.Id).Should().BeEquivalentTo(ids,
            "without a studentId filter the guardians list is unfiltered");
    }

    [TestMethod]
    public async Task ScopedList_CarriesRelationshipCodedValueId_ForLinkedGuardians()
    {
        var tenantId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid(); // arbitrary coded-value id; the
        // handler returns the raw id — name resolution is client-side.

        var (studentId, linkedGuardianId) = await SeedAsync(tenantId, async db =>
        {
            var student = Student.Create("3004", "Rel", "Student", new DateOnly(2014, 5, 1), Guid.NewGuid());
            db.Students.Add(student);
            var linked = Guardian.Create(null, "Linked", "Guardian", null, null, null);
            db.Guardians.Add(linked);
            await db.SaveChangesAsync();

            var s = await db.Students.SingleAsync(x => x.StudentNumber == "3004");
            var l = await db.Guardians.SingleAsync(x => x.FirstName == "Linked");
            db.StudentGuardians.Add(
                StudentGuardian.Create(s.Id, l.Id, GuardianRole.Primary, relationshipId, false, null));
            await db.SaveChangesAsync();
            return (s.Id, l.Id);
        });

        // Scoped list: RelationshipCodedValueId is populated from the link.
        var response = await SendAsync(
            HttpMethod.Get, $"/guardians?studentId={studentId}", tenantId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GuardianDto[]>();
        body.Should().NotBeNull();
        var guardian = body!.Should().ContainSingle("only the linked guardian is returned").Subject;
        guardian.RelationshipCodedValueId.Should().Be(relationshipId,
            "the scoped list carries the relationship coded value id from the student-guardian link");

        // Unfiltered list: RelationshipCodedValueId is null — relationship is
        // per student-guardian link, not a guardian property.
        var unfiltered = await SendAsync(HttpMethod.Get, "/guardians", tenantId);
        unfiltered.StatusCode.Should().Be(HttpStatusCode.OK);
        var allBody = await unfiltered.Content.ReadFromJsonAsync<GuardianDto[]>();
        allBody.Should().NotBeNull();
        allBody!.Should().Contain(g => g.Id == linkedGuardianId);
        allBody.First(g => g.Id == linkedGuardianId).RelationshipCodedValueId.Should().BeNull(
            "the unfiltered tenant-level list does not carry relationship — it is per student-link");
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
