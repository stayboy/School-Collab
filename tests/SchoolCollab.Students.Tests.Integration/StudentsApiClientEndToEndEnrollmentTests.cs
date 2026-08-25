using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// END-TO-END enrollment tests: the ADMIN-SIDE typed client
/// (<see cref="StudentsApiClient.EnrollStudentAsync"/> with the REAL
/// <see cref="TenantPropagationDelegatingHandler"/>) drives an enroll request
/// over HTTP into the hosted students-api (<c>ApiFactory</c>: real Program,
/// real DI, real Postgres via Testcontainers), through
/// <c>EnrollStudentHandler</c>, across the mid-flight settings-api hop
/// (captured stub primary handler), down to the database.
///
/// <para>Verifies the shipped fixes at full fidelity:</para>
/// <list type="bullet">
///   <item><b>Tenant propagation (#181)</b> — the admin-side handler stamps
///         <c>x-tenant-id</c> from the dev selection; the request resolves under
///         that tenant even though the host's DEFAULT tenant differs (the
///         strongest possible signal: without the header the active-period guard
///         fails under the wrong tenant).</item>
///   <item><b>Race-safe materialization (#182)</b> — N concurrent first-time
///         enrolls of the same new CodedValueId against the REAL Postgres unique
///         index <c>ix_grade_levels_tenant_coded_value_id</c>: every call must
///         succeed (no raw DbUpdateException/500) and exactly ONE grade_levels
///         row may exist. (The unit suite simulates the conflict; this test lets
///         the real constraint arbitrate.)</item>
///   <item><b>Fault isolation (#181)</b> — an unavailable dev-tenant store
///         (Redis down → <see cref="IDevTenantSelection"/> throwing) must NOT
///         fail the enroll: the handler proceeds without the header and the
///         receiver falls back to its default tenant.</item>
/// </list>
/// </summary>
[TestClass]
[DoNotParallelize]
public class StudentsApiClientEndToEndEnrollmentTests
{
    private static ApiFactory _baseFactory = default!;
    private static WebApplicationFactory<Program> _factory = default!;
    private static CapturingSettingsHandler _settingsCapture = default!;

    /// <summary>Known stream id the stub settings handler serves.</summary>
    private static readonly Guid StreamCodedValueId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    /// <summary>The grade's CodedValueId — the stub stream's gradeLevel
    /// attribute must reference this for validation to pass.</summary>
    private static readonly Guid GradeCodedValueId = Guid.Parse("22222222-2222-2222-2222-222222222223");

    /// <summary>Dev-selection stub the ADMIN-side propagation handler reads.
    /// Mutable so each test controls the simulated Redis state.</summary>
    private sealed class StubDevTenantSelection : IDevTenantSelection
    {
        public Func<Task<Guid?>>? ReadBehavior { get; set; } =
            () => Task.FromResult<Guid?>(null);
        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default) => ReadBehavior!();
        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static readonly StubDevTenantSelection DevSelection = new();

    private sealed class CapturingSettingsHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            // Echo the REQUESTED id back so both the grade-CV materialization
            // fetch and the stream-CV validation fetch resolve sensibly.
            var pathId = Guid.Empty;
            if (request.RequestUri is { } uri
                && Guid.TryParse(uri.AbsolutePath.Split('/').Last(), out var gid))
            {
                pathId = gid;
            }

            // The stream DTO always carries a gradeLevel attribute pointing at
            // the canonical GradeCodedValueId — tests that enroll under another
            // grade simply omit the stream.
            var dto = new SchoolCollab.Students.Core.Services.StreamCodedValueDto(
                Id: pathId,
                Code: "GRSTREAMS_E2E",
                Name: "Stream E2E",
                Description: null,
                ParentId: null,
                ParentCode: "GRSTREAMS",
                IsDisabled: false,
                DisplayOrder: 1,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                Attributes: new[] { new SchoolCollab.Students.Core.Services.StreamAttributeDto(
                    "gradeLevel", GradeCodedValueId.ToString()) });

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(dto), Encoding.UTF8, "application/json"),
            });
        }
    }

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _baseFactory = new ApiFactory();
        await _baseFactory.InitializeAsync();

        _settingsCapture = new CapturingSettingsHandler();
        _factory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Re-assert the default-tenant fallback ON THE DERIVED factory: tests
            // 2–3 exercise the NO-header path (fault isolation / race) whose
            // fallback must be a real tenant, not Guid.Empty. NOTE the NAMED
            // Configure: TestAuthHandler resolves IOptionsMonitor.Get("TestAuth"),
            // so an UNNAMED Configure<TestAuthHandlerOptions> silently does
            // nothing for the handler.
            services.Configure<TestAuthHandlerOptions>(
                SchoolCollab.Core.Auth.TestAuthExtensions.TestAuthScheme,
                options => options.TenantId = ApiFactory.TestTenantA);

            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _settingsCapture));
        }));
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        if (_baseFactory is not null) await _baseFactory.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE student_enrollments, students, grade_levels, periods CASCADE;");
        DevSelection.ReadBehavior = () => Task.FromResult<Guid?>(null);
    }

    /// <summary>Builds the ADMIN-SIDE client stack exactly as
    /// <c>AddStudentsModule</c> wires it (transient
    /// <see cref="TenantPropagationDelegatingHandler"/> on the typed client).
    /// The PRIMARY handler is the hosted TestServer's in-process dispatcher —
    /// TestServer listens on no TCP port, so real-socket dialing of
    /// <see cref="WebApplicationFactory{TEntryPoint}.Server"/>.BaseAddress would hit
    /// localhost:80. Every ADDITIONAL handler above the primary (tenant
    /// propagation) stays fully real. Deliberately NO global resilience
    /// handler: failures must surface immediately, not be masked by retries.</summary>
    private static ServiceProvider BuildAdminClientStack(HttpMessageHandler serverTransport)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDevTenantSelection>(DevSelection);
        services.TryAddTransient<TenantPropagationDelegatingHandler>();
        services.AddHttpClient<StudentsApiClient>(client =>
                client.BaseAddress = new Uri("http://students-api-test-host/"))
            .ConfigurePrimaryHttpMessageHandler(_ => serverTransport)
            .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();
        // Ctor dependency only — enrichment is not under test here.
        services.AddSingleton(new CodedValuesApiClient(
            new HttpClient { BaseAddress = new Uri("http://settings-api-test-stub/") }));
        return services.BuildServiceProvider();
    }

    [TestMethod]
    public async Task EnrollStudentAsync_PropagatesDevTenant_AllTheWayToBackendAndSettingsHop()
    {
        // Seed EVERYTHING under tenant B while the HOST default stays tenant A.
        // Without the propagated header the enroll would land in tenant A,
        // find no active period, and fail — so success itself proves the
        // admin-side handler stamped x-tenant-id end-to-end.
        var tenantB = ApiFactory.TestTenantB;
        DevSelection.ReadBehavior = () => Task.FromResult<Guid?>(tenantB);

        var (studentId, periodId, _) = await SeedAsync(tenantB, async db =>
        {
            var gradeLevel = GradeLevel.Create(GradeCodedValueId, 1, "Grade 1", 1);
            db.GradeLevels.Add(gradeLevel);

            var period = Period.Create("Term 1", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));
            period.Activate();
            db.Periods.Add(period);

            db.Students.Add(Student.Create("S-E2E-1", "Ann", "Lee", new DateOnly(2015, 1, 1), Guid.NewGuid()));
            await db.SaveChangesAsync();

            var student = await db.Students.SingleAsync(x => x.StudentNumber == "S-E2E-1");
            return (student.Id, period.Id, gradeLevel.Id);
        });

        using var sp = BuildAdminClientStack(_factory.Server.CreateHandler());
        var api = sp.GetRequiredService<StudentsApiClient>();

        var enrollmentId = await api.EnrollStudentAsync(new EnrollStudentRequest(
            StudentId: studentId,
            PeriodId: periodId,
            GradeCodedValueId: GradeCodedValueId,
            StreamCodedValueId: StreamCodedValueId,
            EnrolledOn: DateOnly.FromDateTime(DateTime.UtcNow)));

        enrollmentId.Should().NotBeEmpty("the typed client round-trips the created enrollment id");

        // The settings hop must carry the FORWARDED tenant (students-api's
        // TenantForwardingDelegatingHandler re-stamps what its TestAuthHandler
        // resolved — which was only resolvable because of the admin-side stamp).
        _settingsCapture.LastRequest.Should().NotBeNull(
            "stream validation must run over the settings-api mid-flight hop");
        _settingsCapture.LastRequest!.Headers.GetValues("x-tenant-id").Should()
            .ContainSingle().Which.Should().Be(tenantB.ToString());

        // The row persists under tenant B (NOT the host default A).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        var enrollment = await accessor.RunWithExplicitTenantAsync(tenantB, async _ =>
            await db.StudentEnrollments.AsNoTracking().SingleAsync(e => e.Id == enrollmentId));
        enrollment.StreamCodedValueId.Should().Be(StreamCodedValueId);

        // Row stamping: bypass the tenant filter to inspect TenantId directly —
        // it must be the PROPAGATED tenant B, not the host default A.
        var stampedTenantId = await accessor.RunWithExplicitTenantAsync(tenantB, async _ =>
            await db.StudentEnrollments.AsNoTracking().IgnoreQueryFilters()
                .Where(e => e.Id == enrollmentId)
                .Select(e => e.TenantId)
                .SingleAsync());
        stampedTenantId.Should().Be(tenantB,
            "the enrollment row must be stamped with the PROPAGATED tenant, not the host default");
    }

    [TestMethod]
    public async Task ConcurrentFirstTimeEnrolls_SameNewCodedValue_AllSucceed_SingleGradeLevelRow()
    {
        // Race fix (#182) against the REAL unique index: N concurrent enrolls
        // of the same BRAND-NEW coded value (no grade_levels row seeded, no
        // stream → pure materialization race). Every call must succeed — the
        // loser(s) reuse the winner's row via AddOrReuseAsync instead of
        // surfacing a raw DbUpdateException (500).
        //
        // SIX DISTINCT students drive the race: (tenant, student, period)
        // carries its OWN unique index, so a single student could not enroll
        // twice into the same period even without the materialization race.
        const int parallelCalls = 6;

        var (periodId, studentIds) = await SeedAsync(ApiFactory.TestTenantA, async db =>
        {
            var period = Period.Create("Term 1", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));
            period.Activate();
            db.Periods.Add(period);

            for (var i = 0; i < parallelCalls; i++)
            {
                db.Students.Add(Student.Create($"S-E2E-RACE-{i}", "Race", $"Runner{i}",
                    new DateOnly(2015, 1, 1), Guid.NewGuid()));
            }
            await db.SaveChangesAsync();

            var ids = await db.Students.AsNoTracking()
                .Where(s => s.StudentNumber.StartsWith("S-E2E-RACE"))
                .Select(s => s.Id)
                .ToArrayAsync();
            // NOTE: no grade_levels row is seeded — the raced coded value is
            // generated fresh below and materialized purely by the enroll calls.
            return (period.Id, ids);
        });
        var racedCvId = Guid.NewGuid();

        using var sp = BuildAdminClientStack(_factory.Server.CreateHandler());
        var api = sp.GetRequiredService<StudentsApiClient>();

        var enroll = (Guid sid) => api.EnrollStudentAsync(new EnrollStudentRequest(
            StudentId: sid,
            PeriodId: periodId,
            GradeCodedValueId: racedCvId,
            StreamCodedValueId: null,
            EnrolledOn: DateOnly.FromDateTime(DateTime.UtcNow)));

        var ids = await Task.WhenAll(studentIds.Select(sid => enroll(sid)));

        ids.Should().OnlyContain(id => id != Guid.Empty,
            "EVERY concurrent first-time enroll must succeed — a unique-index loss must be " +
            "absorbed by AddOrReuseAsync, never surfaced as a raw DbUpdateException/500");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        var (gradeLevelRows, enrollmentRows) = await accessor.RunWithExplicitTenantAsync(ApiFactory.TestTenantA, async _ => (
            await db.GradeLevels.AsNoTracking().Where(g => g.CodedValueId == racedCvId).ToListAsync(),
            await db.StudentEnrollments.AsNoTracking().Where(e => studentIds.Contains(e.StudentId)).ToListAsync()));

        gradeLevelRows.Should().ContainSingle(
            "exactly ONE grade_levels row may survive the (tenant, coded_value_id) unique index");
        enrollmentRows.Should().HaveCount(parallelCalls,
            "every racing student must end up enrolled");
        enrollmentRows.Select(e => e.GradeLevelId).Should().OnlyContain(id => id == gradeLevelRows[0].Id,
            "every enrollment references the SAME (winning) GradeLevel row");
    }

    [TestMethod]
    public async Task DevTenantSelectionUnavailable_EnrollStillSucceedsViaDefaultTenantFallback()
    {
        // Fault isolation (#181): the dev-tenant store (Redis) being DOWN must
        // not fail the call. The handler logs a warning and sends WITHOUT the
        // header; the receiving TestAuthHandler falls back to its configured
        // default tenant (A) — which is where this test seeds its data.
        DevSelection.ReadBehavior = () => throw new InvalidOperationException("Redis down (simulated)");

        var (studentId, periodId, _) = await SeedAsync(ApiFactory.TestTenantA, async db =>
        {
            var gradeLevel = GradeLevel.Create(GradeCodedValueId, 1, "Grade 1", 1);
            db.GradeLevels.Add(gradeLevel);

            var period = Period.Create("Term 1", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));
            period.Activate();
            db.Periods.Add(period);

            db.Students.Add(Student.Create("S-E2E-REDIS", "Red", "Isdown", new DateOnly(2015, 1, 1), Guid.NewGuid()));
            await db.SaveChangesAsync();

            var student = await db.Students.SingleAsync(x => x.StudentNumber == "S-E2E-REDIS");
            return (student.Id, period.Id, gradeLevel.Id);
        });

        using var sp = BuildAdminClientStack(_factory.Server.CreateHandler());
        var api = sp.GetRequiredService<StudentsApiClient>();

        var enrollmentId = await api.EnrollStudentAsync(new EnrollStudentRequest(
            StudentId: studentId,
            PeriodId: periodId,
            GradeCodedValueId: GradeCodedValueId,
            StreamCodedValueId: null,
            EnrolledOn: DateOnly.FromDateTime(DateTime.UtcNow)));

        enrollmentId.Should().NotBeEmpty(
            "a dev-tenant cache failure must degrade to 'no header' (receiver falls back), never fail the enroll");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        (await accessor.RunWithExplicitTenantAsync(ApiFactory.TestTenantA, async _ =>
            await db.StudentEnrollments.AsNoTracking().CountAsync(e => e.Id == enrollmentId)))
            .Should().Be(1, "the enrollment persisted under the default-tenant fallback");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static async Task<T> SeedAsync<T>(Guid tenantId, Func<StudentsDbContext, Task<T>> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, async _ => await seed(db));
    }
}
