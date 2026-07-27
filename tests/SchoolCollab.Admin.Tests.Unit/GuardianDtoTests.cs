using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the guardian DTOs after the
/// ContactsEditor migration removed the legacy single-contact
/// <c>PrimaryContact*</c> scalars. The DTOs now carry contacts only via
/// the <c>Contacts</c> collection (top-3, index 0 = preferred) — the
/// per-row <c>PrimaryContactChannel/Value/CountryCode</c> scalars were
/// redundant with <c>Contacts[0]</c> and are gone. These tests guard
/// against re-introducing them.
/// </summary>
[TestClass]
public class GuardianDtoTests
{
    private const string GuardianDtoPath = "GuardianDto.cs";
    private const string StudentGuardianViewDtoPath = "StudentGuardianViewDto.cs";

    [TestMethod]
    public void GuardianDto_Has_Contacts_Collection_Not_PrimaryContact_Scalars()
    {
        var src = ReadDto(GuardianDtoPath);

        // Contacts is the single contact surface (top-3, index 0 = preferred).
        src.Should().Contain("IReadOnlyList<GuardianContactViewDto> Contacts",
            "GuardianDto carries contacts via the Contacts collection");

        // The legacy single-contact scalars must not come back.
        src.Should().NotContain("PrimaryContactChannel",
            "GuardianDto no longer carries the legacy PrimaryContactChannel scalar (Contacts[0] replaces it)");
        src.Should().NotContain("PrimaryContactValue",
            "GuardianDto no longer carries the legacy PrimaryContactValue scalar");
        src.Should().NotContain("PrimaryContactCountryCode",
            "GuardianDto no longer carries the legacy PrimaryContactCountryCode scalar");
    }

    [TestMethod]
    public void StudentGuardianViewDto_Has_Contacts_Collection_Not_PrimaryContact_Scalars()
    {
        var src = ReadDto(StudentGuardianViewDtoPath);

        src.Should().Contain("IReadOnlyList<GuardianContactViewDto> Contacts",
            "StudentGuardianViewDto carries contacts via the Contacts collection");
        src.Should().Contain("TotalContactCount",
            "StudentGuardianViewDto carries TotalContactCount (drives the 'View all' anchor)");

        src.Should().NotContain("PrimaryContactChannel",
            "StudentGuardianViewDto no longer carries the legacy PrimaryContactChannel scalar");
        src.Should().NotContain("PrimaryContactValue",
            "StudentGuardianViewDto no longer carries the legacy PrimaryContactValue scalar");
        src.Should().NotContain("PrimaryContactCountryCode",
            "StudentGuardianViewDto no longer carries the legacy PrimaryContactCountryCode scalar");
    }

    private static string ReadDto(string fileName)
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Core", "DTOs", fileName));
        File.Exists(srcPath).Should().BeTrue(
            $"{fileName} should exist at '{srcPath}' — check the path resolution");
        return File.ReadAllText(srcPath);
    }
}