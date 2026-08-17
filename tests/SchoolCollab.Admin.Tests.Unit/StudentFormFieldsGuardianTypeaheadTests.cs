using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Application.Components.Students;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Model-level tests for the "Add Existing Guardian" typeahead (plan §11.7).
/// These tests follow the §9.4 guidance — test the option MODEL rather than
/// the rendered DOM, since <c>FluentSelect</c> / <c>FluentAutocomplete</c>
/// render as web-component custom elements whose internals are fragile to
/// assert against. The types were made <c>internal</c> and exposed to this
/// test project via <c>InternalsVisibleTo</c> on
/// <c>SchoolCollab.Students.Application.csproj</c>.
/// </summary>
[TestClass]
public class StudentFormFieldsGuardianTypeaheadTests
{
    [TestMethod]
    public void WardLabel_Null_EmDash()
    {
        StudentFormFields.WardLabel(null).Should().Be("—");
    }

    [TestMethod]
    public void WardLabel_One_Singular()
    {
        StudentFormFields.WardLabel(1).Should().Be("+1 ward");
    }

    [TestMethod]
    public void WardLabel_Three_Plural()
    {
        StudentFormFields.WardLabel(3).Should().Be("+3 wards");
    }

    [TestMethod]
    public void WardLabel_Zero_Plural()
    {
        // Zero is plural ("0 wards"), matching the per-ward-count format
        // the landing page uses for unenriched rows.
        StudentFormFields.WardLabel(0).Should().Be("+0 wards");
    }

    [TestMethod]
    public void GuardianSearchRowComparer_SameId_Equal()
    {
        var a = new StudentFormFields.GuardianSearchRow(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            FullName: "Mr. John Smith",
            RelationshipName: null,
            WardCount: null,
            FirstName: "John",
            LastName: "Smith",
            TitleCodedValueId: null,
            RelationshipCodedValueId: null,
            MatchedStudentName: null,
            MatchedStudentNumber: null);
        var b = new StudentFormFields.GuardianSearchRow(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            FullName: "DIFFERENT NAME",
            RelationshipName: "DIFFERENT",
            WardCount: 99,
            FirstName: "Different",
            LastName: "Different",
            TitleCodedValueId: Guid.NewGuid(),
            RelationshipCodedValueId: Guid.NewGuid(),
            MatchedStudentName: "Different",
            MatchedStudentNumber: "X-1");
        StudentFormFields.GuardianSearchRowComparer.Instance.Equals(a, b).Should().BeTrue(
            "two rows with the same Id should compare equal even when other fields differ — this is the §11.2 comparer contract that prevents FluentAutocomplete from dropping the selected row on re-search");
    }

    [TestMethod]
    public void GuardianSearchRowComparer_DifferentId_NotEqual()
    {
        var a = new StudentFormFields.GuardianSearchRow(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            FullName: "Alice",
            RelationshipName: null, WardCount: null,
            FirstName: "Alice", LastName: "A",
            TitleCodedValueId: null, RelationshipCodedValueId: null,
            MatchedStudentName: null, MatchedStudentNumber: null);
        var b = new StudentFormFields.GuardianSearchRow(
            Id: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            FullName: "Alice",
            RelationshipName: null, WardCount: null,
            FirstName: "Alice", LastName: "A",
            TitleCodedValueId: null, RelationshipCodedValueId: null,
            MatchedStudentName: null, MatchedStudentNumber: null);
        StudentFormFields.GuardianSearchRowComparer.Instance.Equals(a, b).Should().BeFalse();
    }

    [TestMethod]
    public void GuardianSearchRowComparer_Null_Safe()
    {
        var a = new StudentFormFields.GuardianSearchRow(
            Id: Guid.NewGuid(), FullName: "x",
            RelationshipName: null, WardCount: null,
            FirstName: "x", LastName: "x",
            TitleCodedValueId: null, RelationshipCodedValueId: null,
            MatchedStudentName: null, MatchedStudentNumber: null);
        StudentFormFields.GuardianSearchRowComparer.Instance.Equals(null, a).Should().BeFalse();
        StudentFormFields.GuardianSearchRowComparer.Instance.Equals(a, null).Should().BeFalse();
        StudentFormFields.GuardianSearchRowComparer.Instance.Equals(null, null).Should().BeTrue();
    }

    [TestMethod]
    public void GuardianSearchRowComparer_GetHashCode_MatchesById()
    {
        var id = Guid.NewGuid();
        var a = new StudentFormFields.GuardianSearchRow(
            Id: id, FullName: "A",
            RelationshipName: null, WardCount: null,
            FirstName: "A", LastName: "A",
            TitleCodedValueId: null, RelationshipCodedValueId: null,
            MatchedStudentName: null, MatchedStudentNumber: null);
        var b = new StudentFormFields.GuardianSearchRow(
            Id: id, FullName: "B",
            RelationshipName: "x", WardCount: 5,
            FirstName: "B", LastName: "B",
            TitleCodedValueId: null, RelationshipCodedValueId: null,
            MatchedStudentName: null, MatchedStudentNumber: null);
        StudentFormFields.GuardianSearchRowComparer.Instance.GetHashCode(a)
            .Should().Be(StudentFormFields.GuardianSearchRowComparer.Instance.GetHashCode(b));
    }

    [TestMethod]
    public void ExistingGuardianSentinel_IsStableAndDistinct()
    {
        // §9.3 sentinel stability: the sentinel Guid must be distinct from
        // anything a relationship coded value could plausibly have. Real CVs
        // use random v4 Guids, so the sentinel (also a random v4) could
        // collide in theory — but the test pins the EXACT value so a
        // regression that changes the sentinel would fail loudly.
        var sentinel = StudentFormFields.ExistingGuardianSentinel;
        sentinel.Should().NotBe(Guid.Empty);
        sentinel.ToString().Should().Be("e1a2b3c4-d5e6-4f7a-8b9c-0d1e2f3a4b5c");
    }

    [TestMethod]
    public void RelationshipOptionKind_HasSentinelAndDivider()
    {
        // §11.2: the option list is sentinel → divider → relationships.
        // The discriminator enum must have all three values.
        var names = System.Enum.GetNames<StudentFormFields.RelationshipOptionKind>();
        names.Should().Contain("Relationship");
        names.Should().Contain("Sentinel");
        names.Should().Contain("Divider");
    }

    [TestMethod]
    public void GuardianSearchRow_ConstructsWithAllElevenFields()
    {
        // Pin the record arity so accidental field removal (e.g. someone
        // drops the §11 match fields) is caught at compile time.
        var id = Guid.NewGuid();
        var title = Guid.NewGuid();
        var rel = Guid.NewGuid();
        var row = new StudentFormFields.GuardianSearchRow(
            Id: id,
            FullName: "Mr. John Smith (Father)",
            RelationshipName: "Father",
            WardCount: 2,
            FirstName: "John",
            LastName: "Smith",
            TitleCodedValueId: title,
            RelationshipCodedValueId: rel,
            MatchedStudentName: "Alice Smith",
            MatchedStudentNumber: "STU-1234");
        row.Id.Should().Be(id);
        row.FullName.Should().Be("Mr. John Smith (Father)");
        row.RelationshipName.Should().Be("Father");
        row.WardCount.Should().Be(2);
        row.FirstName.Should().Be("John");
        row.LastName.Should().Be("Smith");
        row.TitleCodedValueId.Should().Be(title);
        row.RelationshipCodedValueId.Should().Be(rel);
        row.MatchedStudentName.Should().Be("Alice Smith");
        row.MatchedStudentNumber.Should().Be("STU-1234");
    }
}