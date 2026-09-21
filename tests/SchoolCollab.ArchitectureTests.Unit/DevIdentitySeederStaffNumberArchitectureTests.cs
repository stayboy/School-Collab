using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Guards the ar-24 dev-seeder fix (F6): the seeded Dev teacher's <c>staff_number</c> column
/// must carry a non-null dev sentinel rather than the positional <c>NULL</c> it did before.
/// Source-scan of the seeder's INSERT (the repo-root walk-up precedent, like
/// <c>DevIdentitySeederStaffNumberArchitectureTests</c>). Non-vacuity: the file must exist
/// and reference the column; the literal is the discriminator.
/// </summary>
[TestClass]
public class DevIdentitySeederStaffNumberArchitectureTests
{
    private static readonly string SeederPath = Path.Combine(FindRepoRoot(),
        "src", "SchoolCollab.MigrationService", "Seeding", "DevIdentitySeeder.cs");

    private static readonly string SeederSource = File.ReadAllText(SeederPath);

    [TestMethod]
    public void Seeder_StaffNumberIsNotThePositionalNull()
    {
        // The seeder must keep referencing the staff_number column (non-vacuity) while the
        // dev sentinel replaces the positional NULL.
        SeederSource.Should().Contain("staff_number",
            "the seeder INSERT must still name the staff_number column so this check is not vacuous.");

        // The staff_number VALUES position (column 9: {0},NULL,'Dev','Teacher',NULL,NULL,'Dev Teacher',
        // NULL,<staff_number>,NULL,{1}) must no longer be a bare NULL — a clearly dev sentinel proves the fix.
        SeederSource.Should().Contain("'DEV-0001'",
            "the seeded dev teacher staff_number must be 'DEV-0001' (ar-24 F6 / row 5), not a positional NULL.");

        // Ordered fragment (diff-review nit-6): proves the sentinel landed in the staff_number slot
        // (column 9) — not in staff_user_id's slot with column 9 reverted to NULL.
        SeederSource.Should().Contain("'Dev Teacher', NULL, 'DEV-0001', NULL",
            "the VALUES row must read display_name, staff_user_id (NULL), staff_number ('DEV-0001'), level (NULL) in order.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AppHost", "SchoolCollab.AppHost")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
