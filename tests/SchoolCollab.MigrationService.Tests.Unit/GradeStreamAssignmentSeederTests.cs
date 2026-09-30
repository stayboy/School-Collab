using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.MigrationService.Seeding;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.MigrationService.Tests.Unit;

/// <summary>
/// AC5 (revised) — the grade↔stream backfill seeder. Coded values live in the
/// Settings database and the bridge in the Students database, so the backfill can
/// only run in the one scope that has both.
/// </summary>
/// <remarks>
/// <para><b>Owner decisions "configured grades only" + "manual linking" (2026-09-29,
/// after the first implementation flooded the grade-level landing).</b> The seeder is
/// now <b>preservation-only</b>: its single source is the stored legacy
/// <c>gradeLevel</c> attribute, it links only against a <see cref="GradeLevel"/> row
/// the tenant already has, and it <b>never</b> materializes a grade level. There is no
/// CSV mapping any more — a fresh database therefore seeds zero bridge rows.</para>
/// <para>Both contexts are real in-memory providers and the coded values are seeded by
/// the REAL <see cref="CodedValueSeeder"/> from the REAL <c>SeedData</c> files (39
/// GRSTREAMS children, 13 GRADE children, no <c>gradeLevel</c> attributes).</para>
/// </remarks>
[TestClass]
public class GradeStreamAssignmentSeederTests
{
    private const string GradeRCode = "GRADE_R";
    private const string FirstStreamCode = "GRSTREAMS_0A";

    private static string RepoRoot()
    {
        var asmDir = Path.GetDirectoryName(typeof(GradeStreamAssignmentSeederTests).Assembly.Location)!;
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", ".."));
    }

    private static string SeedDataFile(string fileName) =>
        Path.Combine(RepoRoot(), "src", "SchoolCollab.MigrationService", "SeedData", fileName);

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;
        public SettingsDbContext Settings { get; }
        public StudentsDbContext Students { get; }
        public ITenantContextAccessor TenantAccessor { get; }

        public Harness(string name)
        {
            var services = new ServiceCollection();
            services.AddTenancy();
            // Both the CodedValueSeeder and the bridge seeder wrap their batch in one
            // transaction; the in-memory provider has none, and that warning is
            // ERROR-severity by default.
            services.AddDbContext<SettingsDbContext>(o => o
                .UseInMemoryDatabase($"{name}-settings")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            services.AddDbContext<StudentsDbContext>(o => o
                .UseInMemoryDatabase($"{name}-students")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            _provider = services.BuildServiceProvider();

            Settings = _provider.GetRequiredService<SettingsDbContext>();
            Students = _provider.GetRequiredService<StudentsDbContext>();
            TenantAccessor = _provider.GetRequiredService<ITenantContextAccessor>();
        }

        public GradeStreamAssignmentSeeder NewSeeder() =>
            new(Settings, Students, TenantAccessor, NullLogger<GradeStreamAssignmentSeeder>.Instance);

        public async Task SeedCodedValuesAsync()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seeding:FilePath"] = SeedDataFile("seed.csv"),
                ["Seeding:AttributeDefinitionsFilePath"] = SeedDataFile("seed-attribute-definitions.csv"),
                ["Seeding:AttributeValuesFilePath"] = SeedDataFile("seed-attributes.csv"),
            }).Build();

            var seeder = new CodedValueSeeder(
                Settings, configuration, TenantAccessor, NullLogger<CodedValueSeeder>.Instance);
            await seeder.SeedAsync();
        }

        public void Dispose() => _provider.Dispose();
    }

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static Dictionary<string, Guid> Tenants() => new() { ["Hydeson School"] = TenantA };

    private static async Task<CodedValue> GradeCodedValueAsync(Harness h, string code) =>
        await h.Settings.CodedValues
            .IgnoreQueryFilters(["Tenant"])
            .SingleAsync(x => x.Code == code);

    /// <summary>Simulates an UPGRADED database: the legacy attribute is still stored.</summary>
    private static async Task<Guid> StoreLegacyGradeLevelAttributeAsync(
        Harness h, string streamCode, CodedValue gradeCodedValue)
    {
        var stream = await h.Settings.CodedValues
            .Include(x => x.Attributes)
            .IgnoreQueryFilters(["Tenant"])
            .SingleAsync(x => x.Code == streamCode);
        stream.SetAttribute(GradeStreamAssignmentSeeder.LegacyGradeLevelAttributeKey, gradeCodedValue.Id.ToString());
        h.Settings.CodedValues.Update(stream);
        await h.Settings.SaveChangesAsync();
        return stream.Id;
    }

    /// <summary>A grade the tenant has already configured (the only case that links).</summary>
    private static async Task AddGradeLevelAsync(Harness h, Guid tenantId, CodedValue gradeCodedValue)
    {
        using (h.TenantAccessor.SuppressTenantGuard())
        {
            h.Students.GradeLevels.Add(GradeLevel.Create(
                    gradeCodedValue.Id,
                    gradeCodedValue.DisplayOrder,
                    gradeCodedValue.Name,
                    gradeCodedValue.DisplayOrder)
                .WithTenant(tenantId));
            await h.Students.SaveChangesAsync();
        }
    }

    // ── Fresh database: nothing to preserve ─────────────────────────────────

    [TestMethod]
    public async Task FreshDatabase_NoStoredAttributes_InsertsNothing()
    {
        using var h = new Harness("gss-fresh");
        await h.SeedCodedValuesAsync();

        var inserted = await h.NewSeeder().SeedAsync(Tenants());

        inserted.Should().Be(0,
            "a fresh database has no stored gradeLevel attributes, so there is nothing to preserve");
        (await h.Students.GradeStreamAssignments.IgnoreQueryFilters(["Tenant"]).CountAsync()).Should().Be(0);
    }

    // ── "Configured grades only": a pair without a GradeLevel row is skipped ─

    [TestMethod]
    public async Task AttributePair_WithoutAGradeLevelRow_IsSkipped_AndNoGradeIsMaterialized()
    {
        using var h = new Harness("gss-no-grade");
        await h.SeedCodedValuesAsync();
        await StoreLegacyGradeLevelAttributeAsync(h, FirstStreamCode, await GradeCodedValueAsync(h, GradeRCode));

        var inserted = await h.NewSeeder().SeedAsync(Tenants());

        inserted.Should().Be(0,
            "the tenant has never configured Grade R, so the seeder must not link it");
        (await h.Students.GradeLevels.IgnoreQueryFilters(["Tenant"]).CountAsync()).Should().Be(0,
            "the seeder must NEVER materialize a grade_levels row — that is what flooded the grade-level landing");
        (await h.Students.GradeStreamAssignments.IgnoreQueryFilters(["Tenant"]).CountAsync()).Should().Be(0);
    }

    // ── Attribute-derived preservation for a grade the tenant already has ───

    [TestMethod]
    public async Task AttributePair_WithAnExistingGradeLevelRow_IsBackfilled()
    {
        using var h = new Harness("gss-existing-grade");
        await h.SeedCodedValuesAsync();
        var gradeR = await GradeCodedValueAsync(h, GradeRCode);
        var streamId = await StoreLegacyGradeLevelAttributeAsync(h, FirstStreamCode, gradeR);
        await AddGradeLevelAsync(h, TenantA, gradeR);

        var inserted = await h.NewSeeder().SeedAsync(Tenants());

        inserted.Should().Be(1);
        var row = await h.Students.GradeStreamAssignments
            .IgnoreQueryFilters(["Tenant"])
            .SingleAsync();
        row.TenantId.Should().Be(TenantA);
        row.StreamCodedValueId.Should().Be(streamId);

        var gradeLevel = await h.Students.GradeLevels
            .IgnoreQueryFilters(["Tenant"])
            .SingleAsync();
        row.GradeLevelId.Should().Be(gradeLevel.Id);
    }

    [TestMethod]
    public async Task RepeatedRun_InsertsNothing()
    {
        using var h = new Harness("gss-idempotent");
        await h.SeedCodedValuesAsync();
        var gradeR = await GradeCodedValueAsync(h, GradeRCode);
        await StoreLegacyGradeLevelAttributeAsync(h, FirstStreamCode, gradeR);
        await AddGradeLevelAsync(h, TenantA, gradeR);
        var seeder = h.NewSeeder();

        var first = await seeder.SeedAsync(Tenants());
        var second = await seeder.SeedAsync(Tenants());

        first.Should().Be(1);
        second.Should().Be(0, "a re-run must insert nothing (idempotent by the unique index)");
        (await h.Students.GradeStreamAssignments.IgnoreQueryFilters(["Tenant"]).CountAsync()).Should().Be(1);
        (await h.Students.GradeLevels.IgnoreQueryFilters(["Tenant"]).CountAsync()).Should().Be(1,
            "the seeder adds grade levels only when the test created them, never itself");
    }

    // ── Tenant scoping ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task TwoTenants_EachLinkOnlyTheirOwnConfiguredGrade()
    {
        using var h = new Harness("gss-two-tenants");
        await h.SeedCodedValuesAsync();
        var tenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var gradeR = await GradeCodedValueAsync(h, GradeRCode);
        var streamId = await StoreLegacyGradeLevelAttributeAsync(h, FirstStreamCode, gradeR);

        // Only tenant A has configured Grade R.
        await AddGradeLevelAsync(h, TenantA, gradeR);

        var inserted = await h.NewSeeder().SeedAsync(
            new Dictionary<string, Guid> { ["Hydeson School"] = TenantA, ["Little Legends"] = tenantB });

        inserted.Should().Be(1, "only the tenant that already has the grade gets a bridge row");
        var rows = await h.Students.GradeStreamAssignments
            .IgnoreQueryFilters(["Tenant"])
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].TenantId.Should().Be(TenantA);
        rows[0].StreamCodedValueId.Should().Be(streamId);
        (await h.Students.GradeStreamAssignments
            .IgnoreQueryFilters(["Tenant"])
            .CountAsync(x => x.TenantId == tenantB)).Should().Be(0);
    }
}
