using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// Integration tests for <c>POST /students/enrollments</c> with a grade AND a
/// stream against real Postgres (via <see cref="ApiFactory"/>). This is the
/// end-to-end "enroll to grade and stream" round-trip the enroll dialog drives:
/// the endpoint resolves the active period, runs stream validation against the
/// <see cref="GradeStreamAssignment"/> bridge in the Students database, then
/// persists the enrollment.
///
/// <para>Stream validation was moved onto the bridge by the grade-streams round;
/// the settings-api <c>gradeLevel</c> attribute is no longer consulted. The
/// capturing settings handler remains configured so the test can still observe
/// any unexpected mid-flight settings hop, but a successful enrollment now only
/// requires a matching bridge row.</para>
/// </summary>
[TestClass]
[DoNotParallelize]
public class EnrollWithStreamEndpointTests
{
    private static ApiFactory _baseFactory = default!;
    private static WebApplicationFactory<Program> _factory = default!;
    private static HttpClient _client = default!;
    private static CapturingSettingsHandler _settingsCapture = default!;

    /// <summary>Known stream id the stub settings handler serves.</summary>
    private static readonly Guid StreamCodedValueId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    /// <summary>The grade's CodedValueId — the stub stream's gradeLevel
    /// attribute must reference this for validation to pass.</summary>
    private static readonly Guid GradeCodedValueId = Guid.Parse("22222222-2222-2222-2222-222222222223");

    private sealed class CapturingSettingsHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            // Echo the REQUESTED id back: if the endpoint binds StreamCodedValueId
            // incorrectly (e.g. Guid.Empty), this surfaces in the handler's
            // validation flow instead of being masked by a fixed payload.
            var pathId = Guid.Empty;
            if (request.RequestUri is { } uri
                && Guid.TryParse(uri.AbsolutePath.Split('/').Last(), out var gid))
            {
                pathId = gid;
            }

            // Log to stdout — captured per-test by the MSTest runner log.
            Console.WriteLine($"[StubSettings] GET /api/coded-values/{pathId}");

            // Serve the ACTUAL Students.Core DTO serialized with default
            // (PascalCase-retaining) options so binding works regardless of
            // whether the client's ReadFromJsonAsync uses case-sensitive or
            // case-insensitive matching.
            var dto = new SchoolCollab.Students.Core.Services.StreamCodedValueDto(
                Id: pathId,
                Code: "GRSTREAMS_A",
                Name: "Stream A",
                Description: null,
                ParentId: null,
                ParentCode: "GRSTREAMS",
                IsDisabled: false,
                DisplayOrder: 1,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                Attributes: new[] { new SchoolCollab.Students.Core.Services.StreamAttributeDto(
                    "gradeLevel", GradeCodedValueId.ToString()) });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(dto), Encoding.UTF8, "application/json"),
            });
        }
    }

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _settingsCapture = new CapturingSettingsHandler();
        _baseFactory = new ApiFactory();

        // Start the containers + migrations on the base factory first (the
        // delegated factory does not own the Testcontainers lifecycle).
        await _baseFactory.InitializeAsync();

        // WithWebHostBuilder returns a DERIVED (delegated) factory — CreateClient
        // and Services must come from THAT instance, otherwise the test services
        // (the capturing settings-api handler) are never applied and requests hit
        // the real "settings-api" service-discovery host.
        _factory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _settingsCapture))));
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
        if (_baseFactory is not null)
        {
            await _baseFactory.DisposeAsync();
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE student_enrollments, students, grade_levels, grade_stream_assignments, periods CASCADE;");
    }

    [TestMethod]
    public async Task Enroll_WithGradeAndStream_PersistsEnrollment_AndValidatesStreamAgainstBridge()
    {
        var tenantId = ApiFactory.TestTenantA;

        var (studentId, periodId, gradeLevelId) = await SeedAsync(tenantId, async db =>
        {
            var gradeLevel = GradeLevel.Create(GradeCodedValueId, 1, "Grade 7", 1);
            db.GradeLevels.Add(gradeLevel);

            var period = Period.Create("Term 1", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), AcademicYearDivision.None);
            period.Activate();
            db.Periods.Add(period);

            db.Students.Add(Student.Create("S1", "Anna", "Smith", new DateOnly(2015, 1, 1), Guid.NewGuid()));
            await db.SaveChangesAsync();

            var student = await db.Students.SingleAsync(x => x.StudentNumber == "S1");
            return (student.Id, period.Id, gradeLevel.Id);
        });

        // Seed the bridge row that says this grade offers the selected stream.
        // The stream's coded value lives in the Settings database, but the
        // enrollment validation now only checks the Students-side bridge.
        await SeedAsync(tenantId, async db =>
        {
            db.GradeStreamAssignments.Add(GradeStreamAssignment.Create(gradeLevelId, StreamCodedValueId));
            await db.SaveChangesAsync();
            return true;
        });

        var response = await SendAsync(HttpMethod.Post, "/students/enrollments", tenantId,
            new
            {
                StudentId = studentId,
                PeriodId = periodId,
                GradeCodedValueId = GradeCodedValueId,
                StreamCodedValueId = StreamCodedValueId,
                EnrolledOn = (DateOnly?)DateOnly.FromDateTime(DateTime.UtcNow),
            });

        // The enrollment must round-trip: a matching bridge row existed, so
        // stream validation passed and the row was persisted with the stream reference.
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        // Read the enrollment back under the tenant context (rows are
        // tenant-filtered; a bare scope resolves to the default tenant).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        var enrollment = await accessor.RunWithExplicitTenantAsync(tenantId, async _ =>
            await db.StudentEnrollments.AsNoTracking().SingleAsync(
                e => e.StudentId == studentId && e.PeriodId == periodId));
        enrollment.GradeLevelId.Should().Be(gradeLevelId);
        enrollment.StreamCodedValueId.Should().Be(StreamCodedValueId,
            "the enrollment must persist the selected stream");
    }

    [TestMethod]
    public async Task Enroll_WithStreamFromAnotherGrade_IsRejected()
    {
        // The stub stream references GradeCodedValueId, but this test enrolls
        // via a DIFFERENT grade coded value — server-side validation must fail.
        var tenantId = ApiFactory.TestTenantA;
        var otherGradeCodedValueId = Guid.NewGuid();

        var (studentId, periodId, gradeLevelId) = await SeedAsync(tenantId, async db =>
        {
            var gradeLevel = GradeLevel.Create(otherGradeCodedValueId, 1, "Grade 8", 1);
            db.GradeLevels.Add(gradeLevel);

            var period = Period.Create("Term 1", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), AcademicYearDivision.None);
            period.Activate();
            db.Periods.Add(period);

            db.Students.Add(Student.Create("S2", "Bob", "Jones", new DateOnly(2015, 2, 1), Guid.NewGuid()));
            await db.SaveChangesAsync();

            var student = await db.Students.SingleAsync(x => x.StudentNumber == "S2");
            return (student.Id, period.Id, gradeLevel.Id);
        });

        var response = await SendAsync(HttpMethod.Post, "/students/enrollments", tenantId,
            new
            {
                StudentId = studentId,
                PeriodId = periodId,
                GradeCodedValueId = otherGradeCodedValueId,
                StreamCodedValueId = StreamCodedValueId,
                EnrolledOn = (DateOnly?)DateOnly.FromDateTime(DateTime.UtcNow),
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the stream is not offered by the selected grade (no bridge row)");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        db.StudentEnrollments.Should().BeEmpty("a failed stream validation must not persist an enrollment");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static async Task<T> SeedAsync<T>(Guid tenantId, Func<StudentsDbContext, Task<T>> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ => await seed(db));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Guid tenantId, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        return _client.SendAsync(request);
    }
}
