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
/// the endpoint resolves the active period, runs stream validation (which calls
/// the settings-api <c>GET /api/coded-values/{id}</c> through the REAL
/// <see cref="CodedValuesApiClient"/> HttpClient pipeline), then persists the
/// enrollment.
///
/// <para>The settings-api itself is not hosted in this Students-only factory;
/// its HttpClient primary handler is replaced with a capturing stub that serves
/// a stream coded value whose <c>gradeLevel</c> attribute matches the seeded
/// grade. This keeps the real client + handler chain in play while recording
/// the outgoing request for diagnostics. Regression coverage for
/// docs/plans/2026-08-22-tenant-propagation-enroll-stream-investigation.md.</para>
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
            "TRUNCATE TABLE student_enrollments, students, grade_levels, periods CASCADE;");
    }

    [TestMethod]
    public async Task Enroll_WithGradeAndStream_PersistsEnrollment_AndValidatesStreamAgainstSettings()
    {
        var tenantId = ApiFactory.TestTenantA;

        var (studentId, periodId, gradeLevelId) = await SeedAsync(tenantId, async db =>
        {
            var gradeLevel = GradeLevel.Create(GradeCodedValueId, 1, "Grade 7", 1);
            db.GradeLevels.Add(gradeLevel);

            var period = Period.Create("Term 1", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));
            period.Activate();
            db.Periods.Add(period);

            db.Students.Add(Student.Create("S1", "Anna", "Smith", new DateOnly(2015, 1, 1), Guid.NewGuid()));
            await db.SaveChangesAsync();

            var student = await db.Students.SingleAsync(x => x.StudentNumber == "S1");
            return (student.Id, period.Id, gradeLevel.Id);
        });

        // The endpoint uses the Option B contract (commit 7d8a93f): the dialog
        // submits the GRADE CODED VALUE id; the server resolves it to the
        // GradeLevel row (materializing it if missing) and validates the
        // stream's gradeLevel attribute against that coded value.
        var response = await SendAsync(HttpMethod.Post, "/students/enrollments", tenantId,
            new
            {
                StudentId = studentId,
                PeriodId = periodId,
                GradeCodedValueId = GradeCodedValueId,
                StreamCodedValueId = StreamCodedValueId,
                EnrolledOn = (DateOnly?)DateOnly.FromDateTime(DateTime.UtcNow),
            });

        // The enrollment must round-trip: stream validation passed (the real
        // CodedValuesApiClient fetched the stream and the gradeLevel attribute
        // matched) and the row was persisted with the stream reference.
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        // Stream validation must have actually run over the settings-api hop.
        _settingsCapture.LastRequest.Should().NotBeNull(
            "EnrollStudentHandler.ValidateStreamAsync must call the settings-api coded-values endpoint");
        _settingsCapture.LastRequest!.RequestUri!.AbsolutePath.Should()
            .Be($"/api/coded-values/{StreamCodedValueId}");

        // TenantForwardingDelegatingHandler must have forwarded the inbound
        // request's resolved tenant onto the settings-api hop (Class B fix).
        _settingsCapture.LastRequest.Headers.Contains("x-tenant-id").Should().BeTrue(
            "the students-api must forward the enroll request's tenant to the settings-api");
        _settingsCapture.LastRequest.Headers.GetValues("x-tenant-id").Should()
            .ContainSingle().Which.Should().Be(tenantId.ToString());

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

            var period = Period.Create("Term 1", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));
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
            "the stream's gradeLevel attribute references another grade");

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
