using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// The backfill regression the round's static review found MISSING
/// (subject-period-exception-model.md v5 §7.1, P1 and P2-4). Every other suite migrates a
/// <b>fresh, empty</b> container and then truncates, so both backfills in
/// <c>20260928174930_AddPeriodSequenceAndExceptionOrdinal</c> have always executed over
/// <b>zero rows</b> — which is exactly how the P1 defect (a periodised top-level year receiving a
/// position) survived a fully green suite.
///
/// <para><b>The SQL is extracted from the shipped migration file</b>, never retyped: the test
/// finds <c>…_AddPeriodSequenceAndExceptionOrdinal.cs</c> in the repo, regex-matches its two
/// <c>migrationBuilder.Sql("""…""")</c> blocks (asserting there are exactly two, so a silent
/// extraction failure cannot make the test pass vacuously) and executes them, in order, against a
/// real Postgres 16 container <b>already holding rows</b>. A copy of the SQL here would drift from
/// what ships and prove nothing about it.</para>
///
/// <para><b>The schema is the real one</b>, not a hand-made replica: <see cref="ApiFactory"/>
/// runs the whole migration chain, so the columns, types and constraints the backfill writes
/// through are the shipped ones. The rows are seeded <i>after</i> migrating, in the state a real
/// upgrade starts from — every <c>sequence</c>/<c>ordinal</c> NULL, because the migration has just
/// added the columns.</para>
///
/// <para><b>The partial unique index makes this stricter than the migration itself.</b> Because the
/// migration has already run, <c>ix_periods_tenant_parent_division_sequence</c> exists while the
/// backfill executes, so a collision is rejected by the backfill's own <c>UPDATE</c>. The migration
/// instead backfills first and creates the index last, which is why P1's wrong guard could get as
/// far as <c>CreateIndex</c> and abort there.</para>
/// </summary>
[TestClass]
[DoNotParallelize]
public class PeriodSequenceBackfillMigrationTests
{
    private static ApiFactory _factory = default!;

    /// <summary>The shipped migration under test, located by suffix so a rename is a failure to
    /// find rather than a silently skipped test.</summary>
    private const string ShippedMigrationSuffix = "_AddPeriodSequenceAndExceptionOrdinal.cs";

    private static readonly Regex SqlBlockPattern = new(
        "migrationBuilder\\.Sql\\(\\s*\"\"\"(?<sql>.*?)\"\"\"",
        RegexOptions.Singleline | RegexOptions.Compiled);

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _factory = new ApiFactory();
        await _factory.InitializeAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
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

    [TestMethod]
    public async Task Backfill_PositionsSubPeriodRuns_ByParsedNameThenDate_AndLeavesEveryTopLevelYearNull()
    {
        // The P1 shape: a top-level PERIODISED year (division Terms, no parent) is a documented,
        // first-class row — `Period.Division`'s own comment says so — and `sequence` on it is a
        // state no application path can produce (ValidateSequence forbids it). The guard has to be
        // `parent_period_id IS NOT NULL AND division <> 0`; `division <> 0` alone positions the
        // year AND, because such rows are inside the partial index, can abort the migration at
        // CreateIndex when two of them are named with the same trailing integer.
        var tenantId = ApiFactory.TestTenantA;

        var plainYear = await SeedPeriodAsync(tenantId, "2026 Academic Year",
            AcademicYearDivision.None, parentId: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var termsYear = await SeedPeriodAsync(tenantId, "AY2027",
            AcademicYearDivision.Terms, parentId: null,
            new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        // One run, three shapes: a name that PARSES ("Term 1"), a name that does not ("Winter"),
        // and a name that merely ENDS IN A YEAR ("Semester 2 2027") — P2-4. The last one must not
        // become position 2027; it falls through to date order.
        var term1 = await SeedPeriodAsync(tenantId, "Term 1", AcademicYearDivision.Terms, termsYear,
            new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31));
        var winter = await SeedPeriodAsync(tenantId, "Winter", AcademicYearDivision.Terms, termsYear,
            new DateOnly(2027, 4, 1), new DateOnly(2027, 6, 30));
        var semesterYear = await SeedPeriodAsync(tenantId, "Semester 2 2027", AcademicYearDivision.Terms, termsYear,
            new DateOnly(2027, 7, 1), new DateOnly(2027, 9, 30));

        // A SECOND run whose two names parse to the SAME position ("Term 1" vs "Term 01"). The
        // single-row_number() ordering resolves them by date and id; the earlier COALESCE design
        // gave both position 1 and aborted the index.
        var secondYear = await SeedPeriodAsync(tenantId, "AY2026",
            AcademicYearDivision.Terms, parentId: null,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var termOneLeadingZero = await SeedPeriodAsync(tenantId, "Term 1", AcademicYearDivision.Terms, secondYear,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28));
        var term01 = await SeedPeriodAsync(tenantId, "Term 01", AcademicYearDivision.Terms, secondYear,
            new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 30));

        await RunShippedBackfillsAsync(tenantId);

        var periods = await ReadPeriodsAsync(tenantId);

        periods.Single(p => p.Id == plainYear).Sequence.Should().BeNull(
            "a plain top-level year is not the 1st of anything");
        periods.Single(p => p.Id == termsYear).Sequence.Should().BeNull(
            "P1: a PERIODISED top-level year (division Terms, parent NULL) must stay NULL too — "
            + "`division <> 0` alone backfilled a position onto rows ValidateSequence forbids");
        periods.Single(p => p.Id == secondYear).Sequence.Should().BeNull();

        periods.Single(p => p.Id == term1).Sequence.Should().Be(1,
            "a trailing integer in the name beats the date order — 'Term 1' takes position 1 ");
        periods.Single(p => p.Id == winter).Sequence.Should().Be(2,
            "no trailing integer: the position falls back to the run's date order");
        periods.Single(p => p.Id == semesterYear).Sequence.Should().Be(3,
            "P2-4: a name merely ENDING IN A YEAR must not become position 2027 — it is not a "
            + "plausible position in a three-period run, so it falls through to date order");

        periods.Single(p => p.Id == termOneLeadingZero).Sequence.Should().Be(1);
        periods.Single(p => p.Id == term01).Sequence.Should().Be(2,
            "two names parsing to the same integer must still resolve to distinct positions — the "
            + "single row_number() is unique by construction, the earlier COALESCE of two position "
            + "sources was not (both claimed 1 and CreateIndex aborted)");

        periods
            .Where(p => p.ParentPeriodId is not null && p.Sequence is not null)
            .GroupBy(p => (p.ParentPeriodId, p.Division, p.Sequence))
            .Should().OnlyContain(run => run.Count() == 1,
                "one position per (year, division) is the rule the index asserts after the backfill");
    }

    [TestMethod]
    public async Task Backfill_GivesAnOrdinalOnlyToAnExceptionWhoseSpanExactlyMatchesAPositionedSubPeriod()
    {
        // Backfill 2 is deliberately conservative: the ordinal is what a row READS BACK as
        // (decision 15), so guessing "this is probably term 2" is not something a data migration may
        // do. Exact span + same division, or NULL.
        var tenantId = ApiFactory.TestTenantA;

        var termsYear = await SeedPeriodAsync(tenantId, "AY2027",
            AcademicYearDivision.Terms, parentId: null,
            new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));
        await SeedPeriodAsync(tenantId, "Term 1", AcademicYearDivision.Terms, termsYear,
            new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31));
        await SeedPeriodAsync(tenantId, "Term 2", AcademicYearDivision.Terms, termsYear,
            new DateOnly(2027, 4, 1), new DateOnly(2027, 6, 30));

        var gradeLevelId = await SeedGradeLevelAsync(tenantId, "Grade 4");
        var topicId = await SeedTopicAsync(tenantId, "MATH", "Mathematics");

        var exactMatch = await SeedExceptionAsync(tenantId, gradeLevelId, topicId,
            AcademicYearDivision.Terms, new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31));
        var subsetSpan = await SeedExceptionAsync(tenantId, gradeLevelId, topicId,
            AcademicYearDivision.Terms, new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 15));
        var freeWindow = await SeedExceptionAsync(tenantId, gradeLevelId, topicId,
            AcademicYearDivision.None, new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31));
        var wrongDivision = await SeedExceptionAsync(tenantId, gradeLevelId, topicId,
            AcademicYearDivision.Semesters, new DateOnly(2027, 4, 1), new DateOnly(2027, 6, 30));
        var openEnd = await SeedExceptionAsync(tenantId, gradeLevelId, topicId,
            AcademicYearDivision.Terms, new DateOnly(2027, 4, 1), endDate: null);

        await RunShippedBackfillsAsync(tenantId);

        var exceptions = await ReadExceptionsAsync(tenantId);

        exceptions.Single(e => e.Id == exactMatch).Ordinal.Should().Be(1,
            "an exception covering exactly Term 1's span IS the 1st term, so the backfill may say so");
        exceptions.Single(e => e.Id == subsetSpan).Ordinal.Should().BeNull(
            "a 15-day subset of Term 1 is NOT exactly Term 1 — a conservative backfill leaves it alone "
            + "(the ordinal and the span are allowed to disagree, but only a user may make them)");
        exceptions.Single(e => e.Id == freeWindow).Ordinal.Should().BeNull(
            "a free window (division None) has no position in a run of terms");
        exceptions.Single(e => e.Id == wrongDivision).Ordinal.Should().BeNull(
            "the division must match the period's: a Semesters exception does not become Term 2");
        exceptions.Single(e => e.Id == openEnd).Ordinal.Should().BeNull(
            "an open-ended span can never EQUAL a closed sub-period span");
    }

    [TestMethod]
    public void ShippedMigration_DeclaresThePartialIndexTheBackfillsRelyOn_AndExactlyTwoSqlBlocks()
    {
        // The extraction's own guard. A test that extracted ZERO SQL blocks and executed nothing
        // would be as green as one that worked — the lesson the round already paid for with a
        // wrapper that exited 0 while rejecting its own flags. So: exactly two blocks, the first
        // one the positioning statement, and the index the NULL handling depends on declared by
        // the same file.
        var (sequenceBackfill, ordinalBackfill) = ReadShippedBackfills();

        sequenceBackfill.Should().Contain("row_number()",
            "the positions come from ONE ordering over the run — the version with two position "
            + "sources COALESCEd together assigned duplicates and aborted CreateIndex");
        sequenceBackfill.Should().Contain("parent_period_id IS NOT NULL",
            "the P1 guard: a periodised top-level year must be excluded");
        ordinalBackfill.Should().Contain("DISTINCT ON", "a duplicate-span match must be deterministic");

        var source = File.ReadAllText(ShippedMigrationPath());
        source.Should().Contain("filter: \"sequence IS NOT NULL\"",
            "the nullability rules above are only correct because the index is PARTIAL: Postgres "
            + "treats NULLs as DISTINCT, so without the filter every top-level year would collide");
        foreach (var column in new[] { "tenant_id", "parent_period_id", "division", "sequence" })
        {
            source.Should().Contain($"\"{column}\"", "the unique key is one position per year per division");
        }

        // …and the index the running database actually has carries the same predicate.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var indexDef = db.Database
            .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE indexname = 'ix_periods_tenant_parent_division_sequence'")
            .Single();
        indexDef.Should().Contain("UNIQUE").And.Contain("sequence IS NOT NULL");
    }

    // ────── harness ──────

    /// <summary>
    /// Executes the two shipped backfill blocks, in the migration's own order (positions first —
    /// the ordinal backfill READS the sequence values it writes), against the already-migrated
    /// schema.
    /// </summary>
    private async Task RunShippedBackfillsAsync(Guid tenantId)
    {
        var (sequenceBackfill, ordinalBackfill) = ReadShippedBackfills();

        await InTenantAsync(tenantId, async db =>
        {
            await db.Database.ExecuteSqlRawAsync(sequenceBackfill);
            await db.Database.ExecuteSqlRawAsync(ordinalBackfill);
            return true;
        });
    }

    /// <summary>
    /// The two <c>Sql("""…""")</c> blocks, read from the migration file that ships in this
    /// repository — so the test cannot drift from the deployment.
    /// </summary>
    private static (string SequenceBackfill, string OrdinalBackfill) ReadShippedBackfills()
    {
        var source = File.ReadAllText(ShippedMigrationPath());
        var matches = SqlBlockPattern.Matches(source);

        matches.Count.Should().Be(2,
            "the migration ships exactly two backfills (positions, then ordinals); a different "
            + "count means the extraction below is executing something other than what ships");

        var sequenceBackfill = matches[0].Groups["sql"].Value.Trim();
        var ordinalBackfill = matches[1].Groups["sql"].Value.Trim();

        sequenceBackfill.Should().NotBeEmpty();
        ordinalBackfill.Should().NotBeEmpty();
        sequenceBackfill.Should().Contain("SET sequence", "block 1 is the positions backfill");
        ordinalBackfill.Should().Contain("SET ordinal", "block 2 is the ordinals backfill");

        return (sequenceBackfill, ordinalBackfill);
    }

    /// <summary>The shipped migration file, found by walking up to the repo root (the
    /// <c>.slnx</c>) rather than by a hard-coded drive path.</summary>
    private static string ShippedMigrationPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SchoolCollab.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run from inside the repository");

        var migrationsDir = Path.Combine(
            directory!.FullName, "src", "Students", "SchoolCollab.Students.Core", "Migrations");

        var file = Directory.GetFiles(migrationsDir, $"*{ShippedMigrationSuffix}").SingleOrDefault();
        file.Should().NotBeNull(
            $"the migration '*{ShippedMigrationSuffix}' must exist — this test's whole point is that "
            + "it executes the SQL that SHIPS, not a copy of it");
        return file!;
    }

    private async Task<T> InTenantAsync<T>(Guid tenantId, Func<StudentsDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudentsDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        return await accessor.RunWithExplicitTenantAsync(tenantId, _ => work(db));
    }

    // ────── seeds (all positions/ordinals NULL: the state a real upgrade starts from) ──────

    private Task<Guid> SeedPeriodAsync(
        Guid tenantId, string name, AcademicYearDivision division, Guid? parentId, DateOnly start, DateOnly end) =>
        InTenantAsync(tenantId, async db =>
        {
            var period = Period.Create(name, start, end, division, parentId);
            db.Periods.Add(period);
            await db.SaveChangesAsync();
            return period.Id;
        });

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

    private Task<Guid> SeedExceptionAsync(
        Guid tenantId, Guid gradeLevelId, Guid topicId,
        AcademicYearDivision division, DateOnly? startDate, DateOnly? endDate) =>
        InTenantAsync(tenantId, async db =>
        {
            var exception = SubjectEnrollmentException.Create(
                tenantId, gradeLevelId, activityGroupId: null, topicId, division, startDate, endDate);
            db.SubjectEnrollmentExceptions.Add(exception);
            await db.SaveChangesAsync();
            return exception.Id;
        });

    private Task<Period[]> ReadPeriodsAsync(Guid tenantId) =>
        InTenantAsync(tenantId, async db =>
            await db.Periods.AsNoTracking().IgnoreQueryFilters().ToArrayAsync());

    private Task<SubjectEnrollmentException[]> ReadExceptionsAsync(Guid tenantId) =>
        InTenantAsync(tenantId, async db =>
            await db.SubjectEnrollmentExceptions.AsNoTracking().IgnoreQueryFilters().ToArrayAsync());
}
