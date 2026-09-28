using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// Real-Postgres coverage for the subject enrollment exception model at the seams the
/// EF InMemory unit suite structurally cannot reach
/// (subject-period-exception-model.md v3 §2/§6, round AC-1/AC-3/AC-12/AC-18):
///
/// <list type="number">
///   <item><b>The FR-58 feed point (AC-1/AC-8).</b>
///         <c>GET /students/topic-assignments/by-grade/{id}</c> is the exact array
///         <c>TopicAssignmentLookupHttpClient</c> reduces with
///         <c>Any(d => d.TopicId == topicId)</c> for the Assignments publish gate, so an
///         exception whose span CONTAINS the effective date must remove the topic from
///         this response. No test that stubs <c>ITopicAssignmentLookup</c> can see that —
///         only the endpoint can.</item>
///   <item><b>The COALESCE expression index (AC-3, mutation M2).</b> Both span bounds are
///         nullable, so <c>ix_enrollment_exceptions_tenant_grade_topic_span</c> is the
///         only thing that can reject a second open-ended exception with the same start —
///         a plain nullable-column index lets it through because Postgres treats NULLs as
///         DISTINCT. EF InMemory enforces neither expression indexes nor <c>HasFilter</c>,
///         so Postgres is the only witness. The same index's
///         <c>is_deleted = false</c> predicate is what makes remove-then-re-add legal.
///         <b>Handler half vs index half:</b>
///         <c>DuplicateOpenEndedSpan_SameStart_IsRejected409</c> exercises the endpoint,
///         whose 409 comes from the handler's C# pre-check — it never reaches Postgres.
///         <c>TwoIdenticalOpenEndedRows_InsertedDirectly_AreRejectedByTheExpressionIndex</c>
///         is the index half: it bypasses the handler and inserts the two rows through the
///         DbContext, so it is the only test that can observe the expression index.</item>
///   <item><b>The retired Q5 read filter (AC-12(i), mutation M5).</b> The topics listing
///         used to exact-match-filter on the bridge's <c>PeriodId</c>; the discriminator
///         is that a still-supplied <c>?periodId=</c> is now ignored rather than
///         filtering the listing to nothing.</item>
///   <item><b>The <c>/check</c> picker endpoint (AC-18).</b> Containment on a supplied
///         <c>onDate</c>, for both owner forms.</item>
/// </list>
/// </summary>
[TestClass]
[DoNotParallelize]
public class EnrollmentExceptionAvailabilityEndpointTests
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
        var cache = scope.ServiceProvider.GetRequiredService<HybridCache>();

        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE subject_enrollment_exceptions, topic_assignments, activity_groups, grade_levels, subjects, periods CASCADE;");

        // The availability readers cache under the "students" tag; clear it so each test
        // sees the data it just seeded rather than the previous test's array.
        await cache.RemoveByTagAsync("students");
    }

    // ── AC-1/AC-8: the by-grade feed the unchanged Assignments lookup consumes ──

    [TestMethod]
    public async Task ByGradeFeed_SpanContainingTheEffectiveDate_ExcludesTopic()
    {
        var tenantId = ApiFactory.TestTenantA;
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        var topicId = await SeedTopicAsync(tenantId, "MATH", "Mathematics");
        await SeedPeriodLessBridgeRowAsync(tenantId, gradeLevelId, topicId);

        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);

        // Control: with no exception the topic MUST be in the array. This is today's
        // behaviour too, so it also proves the seed is live — a broken seed would make
        // the "absent" assertion below vacuous.
        var unblocked = await GetGradeTopicAssignmentsAsync(tenantId, gradeLevelId, effectiveDate);
        unblocked.Should().ContainSingle(a => a.TopicId == topicId,
            "the by-grade array is the exact feed the Assignments lookup port reduces with Any(d => d.TopicId == topicId)");

        // Act: except the subject for a span containing that date, through the real
        // endpoint. (Pre-round this POST is impossible — the body required a period id —
        // which is the test's pre-round failure mode.)
        var created = await PostAsync("/students/enrollment-exceptions", tenantId,
            new
            {
                gradeLevelId,
                topicId,
                division = AcademicYearDivision.None,
                startDate = effectiveDate.AddDays(-30),
                endDate = effectiveDate.AddDays(30),
            });
        created.StatusCode.Should().Be(HttpStatusCode.Created,
            "a grade-owned exception with a plain window is admissible (§4.2, FR-57 unconstrained)");

        // Assert: the exception removes the topic from that array. FR-58's publish
        // rejection is exactly this exclusion plus the unmodified lookup client — no
        // Assignments-side change is required or permitted.
        var blocked = await GetGradeTopicAssignmentsAsync(tenantId, gradeLevelId, effectiveDate);
        blocked.Should().NotContain(a => a.TopicId == topicId,
            "a span containing the effective date makes the subject unavailable; dropping the NOT EXISTS exception(date) predicate from the availability readers must break this (M1)");
    }

    [TestMethod]
    public async Task ByGradeFeed_SpanNotContainingTheEffectiveDate_LeavesTopicPresent()
    {
        // Proves the predicate is date CONTAINMENT, not "an exception exists".
        var tenantId = ApiFactory.TestTenantA;
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        var topicId = await SeedTopicAsync(tenantId, "MATH", "Mathematics");
        await SeedPeriodLessBridgeRowAsync(tenantId, gradeLevelId, topicId);

        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var created = await PostAsync("/students/enrollment-exceptions", tenantId,
            new
            {
                gradeLevelId,
                topicId,
                division = AcademicYearDivision.None,
                startDate = effectiveDate.AddDays(60),
                endDate = effectiveDate.AddDays(90),
            });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var assignments = await GetGradeTopicAssignmentsAsync(tenantId, gradeLevelId, effectiveDate);
        assignments.Should().Contain(a => a.TopicId == topicId,
            "a span that does not contain the effective date leaves the subject available");
    }

    [TestMethod]
    public async Task ByGradeFeed_OpenEndedSpanStartingToday_ExcludesTopic()
    {
        // The open-bound half: end_date NULL must extend to infinity, which only the
        // availability predicate (not the index) can express.
        var tenantId = ApiFactory.TestTenantA;
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        var topicId = await SeedTopicAsync(tenantId, "MATH", "Mathematics");
        await SeedPeriodLessBridgeRowAsync(tenantId, gradeLevelId, topicId);

        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var created = await PostAsync("/students/enrollment-exceptions", tenantId,
            new
            {
                gradeLevelId,
                topicId,
                division = AcademicYearDivision.None,
                startDate = effectiveDate.AddDays(-1),
                endDate = (DateOnly?)null,
            });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var assignments = await GetGradeTopicAssignmentsAsync(tenantId, gradeLevelId, effectiveDate);
        assignments.Should().NotContain(a => a.TopicId == topicId,
            "an open end excepts every later date");
    }

    // ── AC-12(i): the retired ?periodId= read filter, API-level discriminator ──

    [TestMethod]
    public async Task TopicsByGrade_SuppliedPeriodId_IsIgnoredAndDoesNotFilterTheListing()
    {
        // Pre-round, an extra `?periodId=` on this listing exact-matched the bridge's
        // PeriodId — and since the bridge row carries PeriodId = null, the listing came
        // back EMPTY. Post-round the bridge has no period meaning, so the parameter is
        // ignored and the listing is unchanged. This is the discriminator half of AC-12;
        // the handler test is only a regression guard (F5).
        var tenantId = ApiFactory.TestTenantA;
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        var topicId = await SeedTopicAsync(tenantId, "MATH", "Mathematics");
        await SeedPeriodLessBridgeRowAsync(tenantId, gradeLevelId, topicId);

        var response = await SendAsync(HttpMethod.Get,
            $"/students/subjects/by-grade/{gradeLevelId}?periodId={Guid.NewGuid():D}", tenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var topics = await response.Content.ReadFromJsonAsync<TopicDto[]>();
        topics.Should().Contain(t => t.Id == topicId,
            "M5: re-adding a period filter to ListTopicsByGrade must fail this test");
    }

    // ── AC-3: the COALESCE expression index (mutation M2) ──────────────────────

    [TestMethod]
    public async Task TwoIdenticalOpenEndedRows_InsertedDirectly_AreRejectedByTheExpressionIndex()
    {
        // M2 / AC-3 — THE INDEX HALF, and the only place the expression index can be
        // observed: the endpoint's 409 comes from the handler's ExistsAsync pre-check,
        // which mirrors the COALESCE key in C#, so Postgres is never reached by
        // DuplicateOpenEndedSpan_SameStart_IsRejected409 below. Swapping
        // ix_enrollment_exceptions_tenant_grade_topic_span for a plain nullable-column
        // unique index would leave every other test green.
        //
        // So: bypass the handler, insert two identical OPEN-ENDED rows straight through
        // the DbContext with a single SaveChangesAsync, and require Postgres to reject
        // the batch with SQLSTATE 23505. A plain index on (…, start_date, end_date)
        // cannot collide them — Postgres treats NULL as DISTINCT — while the COALESCE
        // expression index does.
        var tenantId = ApiFactory.TestTenantA;
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        var topicId = await SeedTopicAsync(tenantId, "MATH", "Mathematics");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

        var insertBothOpenEnded = async () => await InTenantAsync(tenantId, async db =>
        {
            db.SubjectEnrollmentExceptions.Add(SubjectEnrollmentException.Create(
                tenantId, gradeLevelId, null, topicId, AcademicYearDivision.None, startDate, endDate: null));
            db.SubjectEnrollmentExceptions.Add(SubjectEnrollmentException.Create(
                tenantId, gradeLevelId, null, topicId, AcademicYearDivision.None, startDate, endDate: null));
            await db.SaveChangesAsync();
            return true;
        });

        var thrown = await insertBothOpenEnded.Should().ThrowAsync<DbUpdateException>(
            "M2: the COALESCE expression index is the only thing that can collide two identical open-ended rows");
        thrown.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be("23505", "the violation must be the unique expression index, not some other constraint");

        // And nothing survived: the batch is a single rejected statement.
        var remaining = await InTenantAsync(tenantId, db =>
            db.SubjectEnrollmentExceptions.CountAsync());
        remaining.Should().Be(0);
    }

    [TestMethod]
    public async Task DuplicateOpenEndedSpan_SameStart_IsRejected409()
    {
        // The case the expression index exists for, observed THROUGH the endpoint:
        // BOTH bounds nullable means Postgres treats NULL as distinct, so only
        // COALESCE(start_date,'-infinity') / COALESCE(end_date,'infinity') can make
        // these two rows collide (M2).
        //
        // NB this is the HANDLER half: the 409 below is thrown by the handler's
        // ExistsAsync pre-check, never by Postgres. The index half — the only place the
        // expression index can be observed — is
        // TwoIdenticalOpenEndedRows_InsertedDirectly_AreRejectedByTheExpressionIndex.
        var tenantId = ApiFactory.TestTenantA;
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        var topicId = await SeedTopicAsync(tenantId, "MATH", "Mathematics");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var body = new
        {
            gradeLevelId,
            topicId,
            division = AcademicYearDivision.None,
            startDate,
            endDate = (DateOnly?)null,
        };

        var first = await PostAsync("/students/enrollment-exceptions", tenantId, body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await PostAsync("/students/enrollment-exceptions", tenantId, body);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the expression unique index forbids a second open-ended exception with the same owner, topic, division and start");
    }

    [TestMethod]
    public async Task RemovedException_CanBeReAdded_ForTheSameOwnerTopicSpan()
    {
        // Without the index's "is_deleted = false" predicate the soft-deleted row would
        // still occupy the unique key and this INSERT would fail with a raw 23505 (a 500).
        var tenantId = ApiFactory.TestTenantA;
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        var topicId = await SeedTopicAsync(tenantId, "MATH", "Mathematics");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var body = new
        {
            gradeLevelId,
            topicId,
            division = AcademicYearDivision.None,
            startDate,
            endDate = startDate.AddDays(14),
        };

        var first = await PostAsync("/students/enrollment-exceptions", tenantId, body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstId = (await first.Content.ReadFromJsonAsync<CreatedId>())!.Id;

        var removed = await SendAsync(HttpMethod.Delete, $"/students/enrollment-exceptions/{firstId}", tenantId);
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "removing an exception soft-deletes the row so the audit trail survives");

        var second = await PostAsync("/students/enrollment-exceptions", tenantId, body);

        second.StatusCode.Should().Be(HttpStatusCode.Created,
            "the unique index excludes soft-deleted rows, so the same span may be re-added after a removal");
        var secondId = (await second.Content.ReadFromJsonAsync<CreatedId>())!.Id;
        secondId.Should().NotBe(firstId, "the re-add must insert a new row, not resurrect the deleted one");
    }

    // ── AC-18: the /check picker endpoint (both owner forms) ───────────────────

    [TestMethod]
    public async Task Check_ReturnsContainmentForOnDate_ForAGradeOwner()
    {
        var tenantId = ApiFactory.TestTenantA;
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        var topicId = await SeedTopicAsync(tenantId, "MATH", "Mathematics");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var endDate = startDate.AddDays(13);

        var created = await PostAsync("/students/enrollment-exceptions", tenantId,
            new
            {
                gradeLevelId,
                topicId,
                division = AcademicYearDivision.Terms,
                startDate,
                endDate,
            });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        (await GetCheckAsync(tenantId, $"gradeLevelId={gradeLevelId:D}", topicId, startDate.AddDays(5)))
            .Should().BeTrue("the span contains that date");
        (await GetCheckAsync(tenantId, $"gradeLevelId={gradeLevelId:D}", topicId, endDate.AddDays(1)))
            .Should().BeFalse("the span does not contain that date (M6: making /check ignore onDate must fail this)");
    }

    [TestMethod]
    public async Task Check_ReturnsContainmentForOnDate_ForAnActivityGroupOwner()
    {
        // Owner gate widened to activity groups (owner decision 2026-09-26), so both
        // sides of the owner toggle need the check.
        var tenantId = ApiFactory.TestTenantA;
        var groupId = await SeedActivityGroupAsync(tenantId, "Robotics Club");
        var topicId = await SeedTopicAsync(tenantId, "ROB", "Robotics");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var endDate = startDate.AddDays(9);

        var created = await PostAsync("/students/enrollment-exceptions", tenantId,
            new
            {
                activityGroupId = groupId,
                topicId,
                division = AcademicYearDivision.None,
                startDate,
                endDate,
            });
        created.StatusCode.Should().Be(HttpStatusCode.Created,
            "an OpenEnded group (the create default) requires a None division for its exception");

        (await GetCheckAsync(tenantId, $"activityGroupId={groupId:D}", topicId, startDate.AddDays(3)))
            .Should().BeTrue();
        (await GetCheckAsync(tenantId, $"activityGroupId={groupId:D}", topicId, startDate.AddDays(-1)))
            .Should().BeFalse();
    }

    // ────── seed helpers (each runs under an explicit tenant) ──────

    private async Task<T> InTenantAsync<T>(Guid tenantId, Func<StudentsDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, _ => work(db));
    }

    private Task<Guid> SeedGradeLevelAsync(Guid tenantId, string name) =>
        InTenantAsync(tenantId, async db =>
        {
            var gradeLevel = GradeLevel.Create(Guid.NewGuid(), 1, name, 1);
            db.GradeLevels.Add(gradeLevel);
            await db.SaveChangesAsync();
            return gradeLevel.Id;
        });

    private Task<Guid> SeedTopicAsync(Guid tenantId, string code, string name) =>
        InTenantAsync(tenantId, async db =>
        {
            var topic = Topic.Create(Guid.NewGuid(), code, name, 1);
            db.Topics.Add(topic);
            await db.SaveChangesAsync();
            return topic.Id;
        });

    private Task<Guid> SeedActivityGroupAsync(Guid tenantId, string name) =>
        InTenantAsync(tenantId, async db =>
        {
            var group = ActivityGroup.Create(name);
            db.ActivityGroups.Add(group);
            await db.SaveChangesAsync();
            return group.Id;
        });

    /// <summary>Bridge row with NO period — the period meaning left the bridge in v3.</summary>
    private Task SeedPeriodLessBridgeRowAsync(Guid tenantId, Guid gradeLevelId, Guid topicId) =>
        InTenantAsync(tenantId, async db =>
        {
            db.GradeTopicAssignments.Add(
                GradeTopicAssignment.Create(gradeLevelId, topicId, DateOnly.FromDateTime(DateTime.UtcNow)));
            await db.SaveChangesAsync();
            return true;
        });

    // ────── request helpers ──────

    private async Task<TopicAssignmentDto[]> GetGradeTopicAssignmentsAsync(
        Guid tenantId, Guid gradeLevelId, DateOnly effectiveDate)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"/students/topic-assignments/by-grade/{gradeLevelId}?effectiveDate={effectiveDate:yyyy-MM-dd}",
            tenantId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TopicAssignmentDto[]>())!;
    }

    private async Task<bool> GetCheckAsync(Guid tenantId, string ownerQuery, Guid topicId, DateOnly onDate)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"/students/enrollment-exceptions/check?{ownerQuery}&topicId={topicId:D}&onDate={onDate:yyyy-MM-dd}",
            tenantId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CheckAnswer>())!.Excepted;
    }

    private Task<HttpResponseMessage> PostAsync(string path, Guid tenantId, object body) =>
        SendAsync(HttpMethod.Post, path, tenantId, body);

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, Guid tenantId, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return _client.SendAsync(request);
    }

    /// <summary>Shape of the <c>POST /students/enrollment-exceptions</c> 201 body (<c>new { id }</c>).</summary>
    private sealed record CreatedId(Guid Id);

    /// <summary>Shape of the <c>GET /students/enrollment-exceptions/check</c> body.</summary>
    private sealed record CheckAnswer(bool Excepted);
}
