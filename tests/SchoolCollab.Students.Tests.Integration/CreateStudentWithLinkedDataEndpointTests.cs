using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// Integration tests for the atomic <c>POST /students/with-linked-data</c> endpoint
/// (Unit of Work). Proves the whole create — student row, guardians (existing + newly
/// created), guardian links, and the optional enrollment — is all-or-nothing: a single
/// disruption rolls back the entire batch, leaving no orphaned student, no partial
/// guardian set, and no "created but not on the grade card" state.
///
/// Runs against the real Students API + Postgres via <see cref="ApiFactory"/>, so the
/// global tenant query filters and real unique constraints apply.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CreateStudentWithLinkedDataEndpointTests
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
            "TRUNCATE TABLE contact_subscriptions, contacts, student_enrollments, " +
            "student_guardians, guardian_name_history, students, guardians, periods, " +
            "grade_levels CASCADE;");
    }

    [TestMethod]
    public async Task ValidCreate_PersistsStudentGuardiansAndEnrollment()
    {
        var tenant = ApiFactory.TestTenantA;
        var grade = await SeedGradeLevelAsync(tenant, 1, "Grade 1");
        var period = await SeedActivePeriodAsync(tenant, "Term 2026");
        var existingGuardian = await SeedGuardianAsync(tenant, "Alice", "Existing");

        // One existing guardian + one newly created guardian + an enrollment target.
        var response = await PostCreateAsync(tenant, new
        {
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateOnly(2015, 1, 1),
            GenderCodedValueId = Guid.NewGuid(),
            Guardians = new[]
            {
                new { ExistingGuardianId = (Guid?)existingGuardian, Role = GuardianRole.Primary, FirstName = (string?)null, LastName = (string?)null, RelationshipCodedValueId = (Guid?)null },
                new { ExistingGuardianId = (Guid?)null, Role = GuardianRole.CC, FirstName = "Sam", LastName = "NewParent", RelationshipCodedValueId = (Guid?)null }
            },
            EnrollmentGradeLevelId = grade
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenant, async _ =>
        {
            var student = await db.Students.SingleAsync(s => s.Id == id);
            student.FirstName.Should().Be("Jane");
            student.LastName.Should().Be("Doe");

            (await db.StudentGuardians.CountAsync(l => l.StudentId == id)).Should().Be(2,
                "the existing and newly created guardians must both be linked");
            (await db.StudentEnrollments.CountAsync(e => e.StudentId == id)).Should().Be(1,
                "the enrollment must be created atomically with the student");

            // The newly created guardian gets its initial name-history snapshot.
            var linkedGuardianIds = await db.StudentGuardians
                .Where(l => l.StudentId == id)
                .Select(l => l.GuardianId)
                .ToArrayAsync();
            var newGuardianId = linkedGuardianIds.Single(g => g != existingGuardian);
            var newGuardian = await db.Guardians.SingleAsync(g => g.Id == newGuardianId);
            newGuardian.FirstName.Should().Be("Sam");
            (await db.GuardianNameHistories.CountAsync(h => h.GuardianId == newGuardianId)).Should().Be(1,
                "the new guardian must have its initial name-history row");
            return true;
        });
    }

    [TestMethod]
    public async Task MissingExistingGuardian_RollsBackWholeCreate()
    {
        var tenant = ApiFactory.TestTenantA;
        var missingGuardian = Guid.NewGuid();

        var response = await PostCreateAsync(tenant, new
        {
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateOnly(2015, 1, 1),
            GenderCodedValueId = Guid.NewGuid(),
            Guardians = new[]
            {
                new { ExistingGuardianId = missingGuardian, Role = GuardianRole.Primary, RelationshipCodedValueId = (Guid?)null }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a missing existing-guardian id must fail the whole create with 404");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenant, async _ =>
        {
            (await db.Students.CountAsync()).Should().Be(0,
                "the student row must be rolled back when a guardian reference is invalid");
            (await db.StudentGuardians.CountAsync()).Should().Be(0);
            (await db.StudentEnrollments.CountAsync()).Should().Be(0);
            return true;
        });
    }

    [TestMethod]
    public async Task MissingEnrollmentGrade_RollsBackWholeCreate()
    {
        var tenant = ApiFactory.TestTenantA;
        var missingGrade = Guid.NewGuid();

        // A new-guardian draft + an invalid enrollment grade: the grade check fails
        // before the transaction, so nothing — student, guardian, or link — is written.
        var response = await PostCreateAsync(tenant, new
        {
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateOnly(2015, 1, 1),
            GenderCodedValueId = Guid.NewGuid(),
            Guardians = new[]
            {
                new { ExistingGuardianId = (Guid?)null, Role = GuardianRole.Primary, FirstName = "Sam", LastName = "Parent", RelationshipCodedValueId = (Guid?)null }
            },
            EnrollmentGradeLevelId = missingGrade
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a missing enrollment grade id must fail the whole create with 404");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenant, async _ =>
        {
            (await db.Students.CountAsync()).Should().Be(0,
                "the student row must be rolled back");
            (await db.Guardians.CountAsync()).Should().Be(0,
                "the newly created guardian must be rolled back too");
            (await db.StudentEnrollments.CountAsync()).Should().Be(0);
            return true;
        });
    }

    [TestMethod]
    public async Task DuplicateExistingGuardianInBatch_RollsBackWholeCreate()
    {
        var tenant = ApiFactory.TestTenantA;
        var existingGuardian = await SeedGuardianAsync(tenant, "Alice", "Existing");

        // Two guardian drafts referencing the SAME existing guardian id — the handler
        // rejects the duplicate before the transaction starts, so the whole create
        // fails with 409 and no student is persisted.
        var response = await PostCreateAsync(tenant, new
        {
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateOnly(2015, 1, 1),
            GenderCodedValueId = Guid.NewGuid(),
            Guardians = new[]
            {
                new { ExistingGuardianId = existingGuardian, Role = GuardianRole.Primary, RelationshipCodedValueId = (Guid?)null },
                new { ExistingGuardianId = existingGuardian, Role = GuardianRole.CC, RelationshipCodedValueId = (Guid?)null }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a duplicate existing-guardian link in the batch must fail the whole create with 409");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenant, async _ =>
        {
            (await db.Students.CountAsync()).Should().Be(0,
                "the student row must be rolled back when a duplicate guardian link is in the batch");
            (await db.StudentGuardians.CountAsync()).Should().Be(0);
            return true;
        });
    }

    // ── seed helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> SeedGradeLevelAsync(Guid tenantId, int level, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var gl = GradeLevel.Create(Guid.NewGuid(), level, name, level);
            db.GradeLevels.Add(gl);
            await db.SaveChangesAsync();
            return gl.Id;
        });
    }

    private async Task<Guid> SeedActivePeriodAsync(Guid tenantId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var period = Period.Create(name, today.AddDays(-1), today.AddDays(1));
            period.Activate();
            db.Periods.Add(period);
            await db.SaveChangesAsync();
            return period.Id;
        });
    }

    private async Task<Guid> SeedGuardianAsync(Guid tenantId, string firstName, string lastName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            // WithTenant before AddInitialNameHistory so the snapshot row carries the tenant.
            var guardian = Guardian.Create(null, firstName, lastName, null, null, null)
                .WithTenant(tenantId);
            guardian.AddInitialNameHistory();
            db.Guardians.Add(guardian);
            db.GuardianNameHistories.Add(guardian.NameHistory[0]);
            await db.SaveChangesAsync();
            return guardian.Id;
        });
    }

    private Task<HttpResponseMessage> PostCreateAsync(Guid tenantId, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/students/with-linked-data")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        return _client.SendAsync(request);
    }

    private sealed record IdResponse(Guid Id);
}
