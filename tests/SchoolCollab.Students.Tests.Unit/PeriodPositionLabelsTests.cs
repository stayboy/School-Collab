using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// <see cref="PeriodPositionLabels"/> — the ONE spelling of a sub-period position
/// (subject-period-exception-model.md v5 §0 decision 15) across the two surfaces that name one:
/// the period form's Sequence row ("2nd term") and the enrollment-exceptions page's position
/// choice ("2nd"). Introduced for the same reason as <c>EnrollmentExceptionLabels</c>: two copies of
/// "which suffix does 21 get" is where they start to disagree.
/// </summary>
[TestClass]
public class PeriodPositionLabelsTests
{
    [DataTestMethod]
    [DataRow(1, "1st")]
    [DataRow(2, "2nd")]
    [DataRow(3, "3rd")]
    [DataRow(4, "4th")]
    [DataRow(5, "5th")]
    [DataRow(11, "11th")]
    [DataRow(12, "12th")]
    [DataRow(13, "13th")]
    [DataRow(21, "21st")]
    [DataRow(22, "22nd")]
    [DataRow(103, "103rd")]
    public void Number_UsesTheOrdinalSuffix_WithTheTeensException(int position, string expected)
    {
        PeriodPositionLabels.Number(position).Should().Be(expected,
            "11th–13th take 'th' whatever their last digit says, so a backfilled position cannot render as 21th");
    }

    [TestMethod]
    public void NumberAndKind_SpellsTheKindFromTheDivision()
    {
        PeriodPositionLabels.NumberAndKind(2, AcademicYearDivision.Semesters).Should().Be("2nd semester");
        PeriodPositionLabels.NumberAndKind(2, AcademicYearDivision.Terms).Should().Be("2nd term");
        PeriodPositionLabels.NumberAndKind(2, AcademicYearDivision.None).Should().Be("2nd term",
            "only a sub-period has a position at all, so the None case is unreachable from the form");
    }
}
