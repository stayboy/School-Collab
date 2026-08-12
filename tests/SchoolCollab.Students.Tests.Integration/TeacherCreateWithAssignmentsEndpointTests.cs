using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// Integration tests for the atomic <c>POST /teachers/with-assignments</c>
/// endpoint (Unit of Work). Proves the whole create — teacher row, qualifications,
/// and every grade/activity link — is all-or-nothing: a single disruption rolls
/// back the entire batch, leaving no orphaned teacher and no partial assignments.
///
/// Runs against the real Students API + Postgres + RabbitMQ via
/// <see cref="ApiFactory"/>, so the global tenant query filters and the real
/// unique constraints apply.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TeacherCreateWithAssignmentsEndpointTests
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
            "TRUNCATE TABLE teacher_activity_assignment_grades, teacher_activity_assignments, " +
            "teacher_grade_levels, teacher_qualifications, teachers, activity_groups, grade_levels CASCADE;");
    }

    [TestMethod]
    public async Task ValidCreate_PersistsTeacherAndAllAssignments()
    {
        var tenant = ApiFactory.TestTenantA;
        var gradeA = await SeedGradeLevelAsync(tenant, 1, "Grade 1");
        var gradeB = await SeedGradeLevelAsync(tenant, 2, "Grade 2");
        var activity = await SeedActivityGroupAsync(tenant, "Choir");

        var response = await PostCreateAsync(tenant, new
        {
            FirstName = "Jane",
            LastName = "Doe",
            QualificationCodedValueIds = new[] { Guid.NewGuid() },
            GradeAssignments = new[]
            {
                new { GradeLevelId = gradeA, SubjectId = (Guid?)null, RoleCodedValueId = (Guid?)null },
                new { GradeLevelId = gradeB, SubjectId = (Guid?)null, RoleCodedValueId = (Guid?)null }
            },
            ActivityAssignments = new[]
            {
                new { ActivityGroupId = activity, RoleCodedValueId = (Guid?)null, GradeLevelIds = new[] { gradeA, gradeB } }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenant, async _ =>
        {
            var teacher = await db.Teachers.SingleAsync(t => t.Id == id);
            teacher.FirstName.Should().Be("Jane");
            teacher.LastName.Should().Be("Doe");

            (await db.TeacherQualifications.CountAsync(q => q.TeacherId == id)).Should().Be(1);
            (await db.TeacherGradeLevels.CountAsync(l => l.TeacherId == id)).Should().Be(2);
            (await db.TeacherActivityAssignments.CountAsync(a => a.TeacherId == id)).Should().Be(1);
            var activityId = (await db.TeacherActivityAssignments.SingleAsync(a => a.TeacherId == id)).Id;
            (await db.TeacherActivityAssignmentGrades.CountAsync(g => g.TeacherActivityAssignmentId == activityId)).Should().Be(2);
            return true;
        });
    }

    [TestMethod]
    public async Task MissingGrade_RollsBackWholeCreate()
    {
        var tenant = ApiFactory.TestTenantA;
        var gradeA = await SeedGradeLevelAsync(tenant, 1, "Grade 1");
        var missingGrade = Guid.NewGuid();

        var response = await PostCreateAsync(tenant, new
        {
            FirstName = "Jane",
            LastName = "Doe",
            GradeAssignments = new[]
            {
                new { GradeLevelId = gradeA, SubjectId = (Guid?)null, RoleCodedValueId = (Guid?)null },
                new { GradeLevelId = missingGrade, SubjectId = (Guid?)null, RoleCodedValueId = (Guid?)null }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a missing grade id must fail the whole create with 404");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenant, async _ =>
        {
            (await db.Teachers.CountAsync()).Should().Be(0,
                "the teacher row must be rolled back when a later assignment fails");
            (await db.TeacherGradeLevels.CountAsync()).Should().Be(0,
                "the valid grade link that preceded the bad one must also be rolled back");
            return true;
        });
    }

    [TestMethod]
    public async Task DuplicateGradeLink_InsideBatch_RollsBackWholeCreate()
    {
        var tenant = ApiFactory.TestTenantA;
        var gradeA = await SeedGradeLevelAsync(tenant, 1, "Grade 1");

        // Two identical grade links (same grade, no subject) in one batch. The
        // handler rejects the duplicate before the transaction starts, so the
        // whole create fails with 409 and no teacher is persisted.
        var response = await PostCreateAsync(tenant, new
        {
            FirstName = "Jane",
            LastName = "Doe",
            GradeAssignments = new[]
            {
                new { GradeLevelId = gradeA, SubjectId = (Guid?)null, RoleCodedValueId = (Guid?)null },
                new { GradeLevelId = gradeA, SubjectId = (Guid?)null, RoleCodedValueId = (Guid?)null }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a duplicate grade link in the batch must fail the whole create with 409");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
        await accessor.RunWithExplicitTenantAsync(tenant, async _ =>
        {
            (await db.Teachers.CountAsync()).Should().Be(0,
                "the teacher row must be rolled back when a duplicate link is in the batch");
            (await db.TeacherGradeLevels.CountAsync()).Should().Be(0);
            return true;
        });
    }

    private async Task<Guid> SeedGradeLevelAsync(Guid tenantId, int level, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var gl = GradeLevel.Create(Guid.NewGuid(), level, name, level);
            db.GradeLevels.Add(gl);
            await db.SaveChangesAsync();
            return gl.Id;
        });
    }

    private async Task<Guid> SeedActivityGroupAsync(Guid tenantId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<SchoolCollab.Core.Tenancy.ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
        {
            var group = ActivityGroup.Create(name);
            db.ActivityGroups.Add(group);
            await db.SaveChangesAsync();
            return group.Id;
        });
    }

    private Task<HttpResponseMessage> PostCreateAsync(Guid tenantId, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/teachers/with-assignments")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        return _client.SendAsync(request);
    }

    private sealed record IdResponse(Guid Id);
}
