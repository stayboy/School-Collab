using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// Real-Postgres coverage for the two columns this round added — <c>periods.sequence</c> and
/// <c>subject_enrollment_exceptions.ordinal</c> (subject-period-exception-model.md v5 §0 decision
/// 15) — at the seams the unit suites structurally cannot reach:
///
/// <list type="number">
///   <item><b>The write path end to end.</b> The preceding run flagged that nothing asserted a
///         posted <c>sequence</c>/<c>ordinal</c> actually persists and reads back; the client
///         records, the command, the DTO projection and the EF mapping each hold one half of that
///         chain, so only an API round trip can see a break in it. Both are asserted here.</item>
///   <item><b>The filtered unique index is the storage half of one-position-per-run.</b> The
///         handler's pre-check (<c>PeriodSequenceGuard</c>, a 422 naming the sibling) means the
///         endpoint never reaches Postgres with a collision, so the index is only observable by
///         bypassing the handler and inserting directly. That is
///         <c>TwoSiblingSubPeriods_InsertedDirectly_AreRejectedByTheFilteredIndex</c>, and it is the
///         only test that would fail if the index were dropped while the guard stayed.</item>
///   <item><b>The ordinal is NOT part of the exception's identity</b> — the COALESCE expression
///         index still keys on the span, so a second row covering the same span with a different
///         ordinal is a 409, not a second exception.</item>
/// </list>
/// </summary>
[TestClass]
[DoNotParallelize]
public class PeriodSequenceAndExceptionOrdinalEndpointTests
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

        await cache.RemoveByTagAsync("students");
    }

    // ── periods.sequence: the round trip ───────────────────────────────────────

    [TestMethod]
    public async Task Create_SubPeriodWithADeclaredPosition_PersistsAndReadsBack()
    {
        var tenantId = ApiFactory.TestTenantA;
        var yearId = await SeedTermsYearAsync(tenantId);
        var termId = await CreateSubPeriodAsync(tenantId, yearId, "Term 3", month: 9, day: 30, sequence: 3);

        var periods = await ListPeriodsAsync(tenantId);

        periods.Single(p => p.Id == termId).Sequence.Should().Be(3,
            "a declared position must survive the client record, the command, the entity and the DTO projection — "
            + "each of which holds one link of the chain, so only the round trip proves the chain");
        periods.Single(p => p.Id == yearId).Sequence.Should().BeNull(
            "a top-level academic year has no position in a run of terms");
        periods.Single(p => p.Id == termId).ParentPeriodId.Should().Be(yearId,
            "the position is scoped to its parent year — the index keys on it");
    }

    [TestMethod]
    public async Task Create_SecondSubPeriod_OnATakenPosition_Is422_NamingTheSibling()
    {
        var tenantId = ApiFactory.TestTenantA;
        var yearId = await SeedTermsYearAsync(tenantId);
        await CreateSubPeriodAsync(tenantId, yearId, "Term 1", month: 1, day: 30, sequence: 1);

        var response = await PostAsync("/students/periods", tenantId, new CreatePeriod(
            "Another first term",
            new DateOnly(2027, 3, 1),
            new DateOnly(2027, 6, 30),
            AcademicYearDivision.Terms,
            ParentPeriodId: yearId,
            Sequence: 1));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "a taken position is a pre-check rejection, not the filtered index's unhandled DbUpdateException (a 500)");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Term 1",
            "the caller can only act on the rejection if it says WHICH sibling holds the position");
        body.Should().Contain("position 1");
    }

    [TestMethod]
    public async Task Update_SubPeriod_KeepingItsOwnPosition_Is204_AndOntoASiblingsIs422()
    {
        var tenantId = ApiFactory.TestTenantA;
        var yearId = await SeedTermsYearAsync(tenantId);
        var term1 = await CreateSubPeriodAsync(tenantId, yearId, "Term 1", month: 1, day: 30, sequence: 1);
        await CreateSubPeriodAsync(tenantId, yearId, "Term 2", month: 4, day: 30, sequence: 2);

        // Keeping its own position is not a collision with itself — an edit form must be savable.
        var kept = await PutAsync(tenantId, term1, new
        {
            name = "Term 1 (renamed)",
            startDate = "2027-01-01",
            endDate = "2027-02-28",
            parentPeriodId = yearId,
            sequence = 1,
        });
        kept.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListPeriodsAsync(tenantId)).Single(p => p.Id == term1).Sequence.Should().Be(1);

        // Taking a sibling's position is not.
        var taken = await PutAsync(tenantId, term1, new
        {
            name = "Term 1",
            startDate = "2027-01-01",
            endDate = "2027-02-28",
            parentPeriodId = yearId,
            sequence = 2,
        });
        taken.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await taken.Content.ReadAsStringAsync()).Should().Contain("Term 2",
            "the sibling holding position 2 is named");
    }

    // ── the position's BOUNDARY rules: an illegal position is a 422, not a 400 ──

    [TestMethod]
    public async Task Create_SubPeriod_WithAPositionBelowOne_Is422_Not400()
    {
        // P2-3 of the round's static review: the entity rejects this with ArgumentException, which
        // these routes map to 400, while every other period shape rule (and the taken-position rule
        // above) is a 422. Positions are 1-based, so 0 is not a value the caller could have meant.
        var tenantId = ApiFactory.TestTenantA;
        var yearId = await SeedTermsYearAsync(tenantId);

        var response = await PostAsync("/students/periods", tenantId, new CreatePeriod(
            "Zeroth term",
            new DateOnly(2027, 1, 1),
            new DateOnly(2027, 2, 28),
            AcademicYearDivision.Terms,
            ParentPeriodId: yearId,
            Sequence: 0));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "an impossible position belongs in the same status family as a taken one");
        (await response.Content.ReadAsStringAsync()).Should().Contain("1 or greater");
        (await ListPeriodsAsync(tenantId)).Should().NotContain(p => p.ParentPeriodId == yearId);
    }

    [TestMethod]
    public async Task Create_TopLevelYear_WithAPosition_Is422_Not400()
    {
        // A year is not the 2nd term of anything — the position belongs to a sub-period (the
        // entity's "only a sub-period can carry a sequence" rule, reached through the boundary).
        var tenantId = ApiFactory.TestTenantA;

        var response = await PostAsync("/students/periods", tenantId, new CreatePeriod(
            "AY2027",
            new DateOnly(2027, 1, 1),
            new DateOnly(2027, 12, 31),
            AcademicYearDivision.Terms,
            ParentPeriodId: null,
            Sequence: 2));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("top-level academic year");
        (await ListPeriodsAsync(tenantId)).Should().BeEmpty("the rejected create wrote nothing");
    }

    [TestMethod]
    public async Task Update_TopLevelYear_WithAPosition_Is422_Not400()
    {
        var tenantId = ApiFactory.TestTenantA;
        var yearId = await SeedTermsYearAsync(tenantId);

        var response = await PutAsync(tenantId, yearId, new
        {
            name = "AY2027",
            startDate = "2027-01-01",
            endDate = "2027-12-31",
            parentPeriodId = (Guid?)null,
            sequence = 1,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ListPeriodsAsync(tenantId)).Single(p => p.Id == yearId).Sequence.Should().BeNull(
            "the rejected edit left the year as it was");
    }

    // ── the filtered unique index: the storage half ────────────────────────────

    [TestMethod]
    public async Task TwoSiblingSubPeriods_InsertedDirectly_AreRejectedByTheFilteredIndex()
    {
        // The handler's pre-check means the ENDPOINT never reaches Postgres with a collision, so
        // the index can only be observed by bypassing the handler: two sibling rows of one year
        // and division, both claiming position 1. Swapping the filtered unique index for a plain
        // one (or dropping the filter) would leave every other test in this file green.
        var tenantId = ApiFactory.TestTenantA;
        var yearId = await SeedTermsYearAsync(tenantId);

        var insertBoth = async () => await InTenantAsync(tenantId, async db =>
        {
            db.Periods.Add(Period.Create(
                "Term 1", new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31),
                AcademicYearDivision.Terms, parentPeriodId: yearId, sequence: 1));
            db.Periods.Add(Period.Create(
                "Winter", new DateOnly(2027, 4, 1), new DateOnly(2027, 6, 30),
                AcademicYearDivision.Terms, parentPeriodId: yearId, sequence: 1));
            await db.SaveChangesAsync();
            return true;
        });

        var thrown = await insertBoth.Should().ThrowAsync<DbUpdateException>(
            "one position per (year, division) is the rule; only the filtered unique index can hold it against a race");
        thrown.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be("23505");

        (await ListPeriodsAsync(tenantId)).Should().NotContain(p => p.ParentPeriodId == yearId,
            "the batch is one rejected statement, so nothing was positioned");
    }

    [TestMethod]
    public async Task SiblingSubPeriods_WithoutPositions_DoNotCollide()
    {
        // The index is FILTERED on sequence IS NOT NULL, which is what makes an unpositioned
        // sub-period legal — and what makes the many unpositioned rows a tenant already has (the
        // atomic year create and the grid's auto-split both leave the position null) harmless.
        var tenantId = ApiFactory.TestTenantA;
        var yearId = await SeedTermsYearAsync(tenantId);
        await CreateSubPeriodAsync(tenantId, yearId, "Winter", month: 1, day: 30, sequence: null);
        await CreateSubPeriodAsync(tenantId, yearId, "Spring", month: 4, day: 30, sequence: null);

        var subPeriods = (await ListPeriodsAsync(tenantId)).Where(p => p.ParentPeriodId == yearId).ToArray();
        subPeriods.Should().HaveCount(2);
        subPeriods.Should().OnlyContain(p => p.Sequence == null, "NULLs never collide under a filtered unique index");
    }

    // ── subject_enrollment_exceptions.ordinal: the round trip ──────────────────

    [TestMethod]
    public async Task Create_ExceptionWithAnOrdinal_PersistsAndReadsBack()
    {
        var tenantId = ApiFactory.TestTenantA;
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        var topicId = await SeedTopicAsync(tenantId, "MATH", "Mathematics");

        var created = await PostAsync("/students/enrollment-exceptions", tenantId, new
        {
            gradeLevelId,
            topicId,
            division = AcademicYearDivision.Terms,
            startDate = "2027-03-01",
            endDate = "2027-06-30",
            ordinal = 2,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var rows = await ListExceptionsAsync(tenantId, gradeLevelId);

        rows.Should().ContainSingle();
        rows.Single().Ordinal.Should().Be(2,
            "the ordinal is PERSISTED (Q6=B): a row renders \"2nd term\" without re-deriving it from the dates, "
            + "and it travels through the client record, the command, the entity and the DTO projection");
        rows.Single().Division.Should().Be("Terms");
    }

    [TestMethod]
    public async Task Create_ExceptionWithADifferentOrdinalForTheSameSpan_Is409()
    {
        // The ordinal is descriptive and deliberately NOT part of the uniqueness key: two rows
        // covering the same span for the same owner, topic and division are the same exception
        // whatever positions they claim, so letting the ordinal vary must not buy a second row.
        var tenantId = ApiFactory.TestTenantA;
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        var topicId = await SeedTopicAsync(tenantId, "MATH", "Mathematics");

        var first = await PostAsync("/students/enrollment-exceptions", tenantId, new
        {
            gradeLevelId,
            topicId,
            division = AcademicYearDivision.Terms,
            startDate = "2027-03-01",
            endDate = "2027-06-30",
            ordinal = 1,
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await PostAsync("/students/enrollment-exceptions", tenantId, new
        {
            gradeLevelId,
            topicId,
            division = AcademicYearDivision.Terms,
            startDate = "2027-03-01",
            endDate = "2027-06-30",
            ordinal = 3,
        });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the COALESCE expression index keys on the span, never on the ordinal");
        (await ListExceptionsAsync(tenantId, gradeLevelId)).Should().ContainSingle();
    }

    [TestMethod]
    public async Task Create_ExceptionWithAnOrdinalOnAFreeWindow_Is422()
    {
        // A free window is not a run of terms, so "the 2nd" of one is a category error, not a
        // value the server could store and ignore (§0 decision 15, ValidateExceptionOrdinal).
        var tenantId = ApiFactory.TestTenantA;
        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 1");
        var topicId = await SeedTopicAsync(tenantId, "MATH", "Mathematics");

        var response = await PostAsync("/students/enrollment-exceptions", tenantId, new
        {
            gradeLevelId,
            topicId,
            division = AcademicYearDivision.None,
            startDate = "2027-03-01",
            endDate = "2027-06-30",
            ordinal = 2,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("real part");
    }

    // ────── seed helpers (each runs under an explicit tenant) ──────

    private async Task<T> InTenantAsync<T>(Guid tenantId, Func<StudentsDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, _ => work(db));
    }

    /// <summary>A Terms academic year covering 2027, created through the API.</summary>
    private async Task<Guid> SeedTermsYearAsync(Guid tenantId)
    {
        var response = await PostAsync("/students/periods", tenantId,
            new CreatePeriod("AY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), AcademicYearDivision.Terms));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreatedPeriod>();
        return created!.Id;
    }

    private async Task<Guid> CreateSubPeriodAsync(
        Guid tenantId, Guid yearId, string name, int month, int day, int? sequence)
    {
        var response = await PostAsync("/students/periods", tenantId, new CreatePeriod(
            name,
            new DateOnly(2027, month, 1),
            new DateOnly(2027, month, day),
            AcademicYearDivision.Terms,
            ParentPeriodId: yearId,
            Sequence: sequence));

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the sub-period create is the path under test");
        return (await response.Content.ReadFromJsonAsync<CreatedPeriod>())!.Id;
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

    // ────── request helpers ──────

    private async Task<PeriodDto[]> ListPeriodsAsync(Guid tenantId)
    {
        var response = await SendAsync(HttpMethod.Get, "/students/periods", tenantId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PeriodDto[]>())!;
    }

    private async Task<SubjectEnrollmentExceptionDto[]> ListExceptionsAsync(Guid tenantId, Guid gradeLevelId)
    {
        var response = await SendAsync(HttpMethod.Get,
            $"/students/enrollment-exceptions?gradeLevelId={gradeLevelId:D}", tenantId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SubjectEnrollmentExceptionDto[]>())!;
    }

    private Task<HttpResponseMessage> PostAsync(string path, Guid tenantId, object body) =>
        SendAsync(HttpMethod.Post, path, tenantId, body);

    private Task<HttpResponseMessage> PutAsync(Guid tenantId, Guid periodId, object body) =>
        SendAsync(HttpMethod.Put, $"/students/periods/{periodId:D}", tenantId, body);

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Guid tenantId, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("x-tenant-id", tenantId.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return _client.SendAsync(request);
    }

    /// <summary>Shape of the <c>POST /students/periods</c> 201 body (<c>new { id, subPeriodIds }</c>).</summary>
    private sealed record CreatedPeriod(Guid Id);
}
