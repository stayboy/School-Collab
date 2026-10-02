using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Api;
using SchoolCollab.Students.Core.CQRS.Students.Queries.ResolveStudentsByTarget;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Api.Tests.Unit;

/// <summary>
/// R2-7 (TGT-8 / D-7): wire tests for <code>GET /students/by-target</code>.
/// Covers repeated query-array binding, all four id-array legs (union + dedupe),
/// archived-group exclusion, soft-deleted student exclusion, the grade-agnostic
/// stream leg, and that the route inherits the students group's authorization.
/// </summary>
[TestClass]
public class StudentTargetRoutesTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GradeA = Guid.Parse("22222222-2222-2222-2222-22222222222a");
    private static readonly Guid GradeB = Guid.Parse("22222222-2222-2222-2222-22222222222b");
    private static readonly Guid StreamId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GenderMale = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private sealed class StubFeatureFlags : IFeatureFlagService
    {
        public bool IsEnabled(string featureKey) => featureKey == FeatureFlagKeys.DisableOIDCAuth;
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) =>
            Task.FromResult(IsEnabled(featureKey));
    }

    private sealed class StubTenantProvider : ITenantProvider
    {
        public TenantContext GetTenantContext() => new(TenantId, "TestSchool", TenantType.School);
    }

    private static WebApplication BuildApp(out HttpClient client)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var dbName = "students-by-target-" + Guid.NewGuid();
        builder.Services.AddTenancy();
        builder.Services.AddDbContext<StudentsDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddScoped<IQueryHandler<ResolveStudentsByTarget, Guid[]>, ResolveStudentsByTargetHandler>();
        builder.Services.AddSingleton<IFeatureFlagService>(new StubFeatureFlags());
        builder.Services.Replace(ServiceDescriptor.Singleton<ITenantProvider>(new StubTenantProvider()));

        var app = builder.Build();
        app.MapStudentEndpoints(app.Services.GetRequiredService<IFeatureFlagService>());
        app.StartAsync().GetAwaiter().GetResult();

        var server = (TestServer)app.Services.GetRequiredService<IServer>();
        client = server.CreateClient();

        return app;
    }

    private static WebApplicationFactory<Program> NewAuthFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("FeatureFlags:FEATURE:DisableOIDCAuth", "false");
            builder.UseSetting("ConnectionStrings:students-db",
                "Host=localhost;Database=students_placeholder;Username=test;Password=test");
            builder.UseSetting("ConnectionStrings:settings-db",
                "Host=localhost;Database=settings_placeholder;Username=test;Password=test");
            builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");
            builder.UseSetting("Outbox:ExchangeName", "students");
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        });

    private static async Task<(StudentsDbContext db, AsyncServiceScope scope)> SeedAsync(IServiceProvider sp)
    {
        var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var period = Period.Create("2026", today.AddDays(-10), today.AddDays(10), AcademicYearDivision.Terms);
        db.Periods.Add(period);

        var byGradeA = NewStudent("S1");
        var byGradeBStream = NewStudent("S2");
        var byGroup = NewStudent("S3");
        var explicitStudent = NewStudent("S4");
        var deleted = NewStudent("S5");
        deleted.Delete();

        db.Students.AddRange(byGradeA, byGradeBStream, byGroup, explicitStudent, deleted);

        db.StudentEnrollments.AddRange(
            StudentEnrollment.Create(byGradeA.Id, period.Id, GradeA, streamCodedValueId: null).WithTenant(TenantId),
            StudentEnrollment.Create(byGradeBStream.Id, period.Id, GradeB, streamCodedValueId: StreamId).WithTenant(TenantId),
            StudentEnrollment.Create(byGroup.Id, period.Id, GradeA, streamCodedValueId: null).WithTenant(TenantId),
            StudentEnrollment.Create(explicitStudent.Id, period.Id, GradeB, streamCodedValueId: null).WithTenant(TenantId));

        var activeGroup = ActivityGroup.Create("Chess").WithTenant(TenantId);
        var archivedGroup = ActivityGroup.Create("Old Club").WithTenant(TenantId);
        archivedGroup.Deactivate();
        db.ActivityGroups.AddRange(activeGroup, archivedGroup);

        db.ActivityGroupMemberships.Add(
            ActivityGroupMembership.Create(activeGroup.Id, byGroup.Id).WithTenant(TenantId));
        db.ActivityGroupMemberships.Add(
            ActivityGroupMembership.Create(archivedGroup.Id, byGroup.Id).WithTenant(TenantId));

        await db.SaveChangesAsync();
        return (db, scope);
    }

    private static Student NewStudent(string number) =>
        Student.Create(number, "A", number, new DateOnly(2015, 1, 1), GenderMale).WithTenant(TenantId);

    [TestMethod]
    public async Task ByTarget_WithoutAuth_IsChallenged()
    {
        using var factory = NewAuthFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/students/by-target?allStudents=true");

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden },
            "the route inherits the students group's RequireAuthorization");
    }

    [TestMethod]
    public async Task ByTarget_AllStudents_ReturnsEveryNonDeletedStudent()
    {
        await using var app = BuildApp(out var client);
        var (_, scope) = await SeedAsync(app.Services);
        await using (scope)
        {
            var response = await client.GetAsync("/students/by-target?allStudents=true");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var ids = await DeserializeIdsAsync(response);

            ids.Should().HaveCount(4, "D-6: allStudents returns every non-soft-deleted student in the tenant");
        }
    }

    [TestMethod]
    public async Task ByTarget_RepeatedQueryArray_BindsAllValues()
    {
        await using var app = BuildApp(out var client);
        var (_, scope) = await SeedAsync(app.Services);
        await using (scope)
        {
            var url = $"/students/by-target?allStudents=false&gradeLevelIds={GradeA}&gradeLevelIds={GradeB}";
            var response = await client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var ids = await DeserializeIdsAsync(response);

            ids.Should().HaveCount(4, "repeated query-array binding unions both grade legs (no overlap in seed)");
        }
    }

    [TestMethod]
    public async Task ByTarget_AllFourArrays_UnionsAndDedupes()
    {
        await using var app = BuildApp(out var client);
        var (db, scope) = await SeedAsync(app.Services);
        await using (scope)
        {
            var byGradeA = db.Students.Single(s => s.StudentNumber == "S1").Id;
            var activeGroup = db.ActivityGroups.Single(g => g.Name == "Chess").Id;

            var url = $"/students/by-target?allStudents=false&gradeLevelIds={GradeA}&streamCodedValueIds={StreamId}&studentIds={byGradeA}&activityGroupIds={activeGroup}";
            var response = await client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var ids = await DeserializeIdsAsync(response);

            ids.Should().HaveCount(3, "TGT-3: a student matching several legs appears once");
        }
    }

    [TestMethod]
    public async Task ByTarget_ArchivedGroup_IsExcluded()
    {
        await using var app = BuildApp(out var client);
        var (db, scope) = await SeedAsync(app.Services);
        await using (scope)
        {
            var archivedGroup = db.ActivityGroups.Single(g => g.Name == "Old Club").Id;

            var response = await client.GetAsync($"/students/by-target?allStudents=false&activityGroupIds={archivedGroup}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var ids = await DeserializeIdsAsync(response);

            ids.Should().BeEmpty("EC-4: an archived group resolves to nobody");
        }
    }

    [TestMethod]
    public async Task ByTarget_Stream_IsGradeAgnostic()
    {
        await using var app = BuildApp(out var client);
        var (_, scope) = await SeedAsync(app.Services);
        await using (scope)
        {
            var response = await client.GetAsync($"/students/by-target?allStudents=false&streamCodedValueIds={StreamId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var ids = await DeserializeIdsAsync(response);

            ids.Should().ContainSingle(id => true, "TGT-5: the stream leg carries no grade predicate");
        }
    }

    [TestMethod]
    public async Task ByTarget_ExplicitStudent_ExcludesSoftDeleted()
    {
        await using var app = BuildApp(out var client);
        var (db, scope) = await SeedAsync(app.Services);
        await using (scope)
        {
            var kept = db.Students.Single(s => s.StudentNumber == "S4").Id;
            var deleted = db.Students.IgnoreQueryFilters().Single(s => s.StudentNumber == "S5").Id;

            var response = await client.GetAsync($"/students/by-target?allStudents=false&studentIds={kept}&studentIds={deleted}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var ids = await DeserializeIdsAsync(response);

            ids.Should().BeEquivalentTo(new[] { kept }, "TGT-6: soft-deleted students never match");
        }
    }

    private static async Task<Guid[]> DeserializeIdsAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Guid[]>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    }
}
