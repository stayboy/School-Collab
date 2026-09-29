using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.TopicAssignments;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// <see cref="TopicAssignmentPeriodValidator.ValidateExceptionOrdinal"/> — the exception's
/// ordinal invariants (subject-period-exception-model.md v5 §0 decision 15).
///
/// <para>Two rules, both about the ordinal being a 1-based position IN a run: it must be
/// <c>1</c> or greater, and it may only be supplied when the division names a real part (a
/// free window has no "3rd"). What the validator deliberately does NOT do is cross-check the
/// ordinal against the span — the two are allowed to disagree, which is why there is no
/// "the ordinal must match the dates" test here: that behaviour is the ABSENCE of a rule, and
/// it is asserted at the point where it matters (the handler persists a 3rd-term ordinal
/// beside typed dates; see <c>CreateSubjectEnrollmentExceptionHandlerTests</c>).</para>
/// </summary>
[TestClass]
public class TopicAssignmentPeriodOrdinalTests
{
    [TestMethod]
    public void ValidateExceptionOrdinal_RealPartWithAPosition_IsAccepted()
    {
        // The rule is about the DIVISION being a real part, never about whether a period of
        // that position exists — that is Q1 (a 3rd term may be named before it is created).
        Action termsThree = () => TopicAssignmentPeriodValidator
            .ValidateExceptionOrdinal(AcademicYearDivision.Terms, 3);
        Action semesterOne = () => TopicAssignmentPeriodValidator
            .ValidateExceptionOrdinal(AcademicYearDivision.Semesters, 1);

        termsThree.Should().NotThrow();
        semesterOne.Should().NotThrow();
    }

    [TestMethod]
    public void ValidateExceptionOrdinal_NoOrdinal_IsAlwaysAccepted()
    {
        Action onATerm = () => TopicAssignmentPeriodValidator
            .ValidateExceptionOrdinal(AcademicYearDivision.Terms, null);
        Action onAFreeWindow = () => TopicAssignmentPeriodValidator
            .ValidateExceptionOrdinal(AcademicYearDivision.None, null);

        onATerm.Should().NotThrow("the ordinal is optional: an exception may be a pure span");
        onAFreeWindow.Should().NotThrow();
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void ValidateExceptionOrdinal_NonPositive_Throws422(int ordinal)
    {
        var act = () => TopicAssignmentPeriodValidator.ValidateExceptionOrdinal(AcademicYearDivision.Terms, ordinal);

        act.Should().Throw<TopicAssignmentPeriodException>()
            .Which.Message.Should().Contain("1 or greater", "positions are 1-based (0 is not a position)");
    }

    [TestMethod]
    public void ValidateExceptionOrdinal_OnAFreeWindow_Throws422()
    {
        // "the 3rd of a free window" is a category error, not a value the server could store
        // and ignore: a free window is not a run of terms, so it has no positions at all.
        var act = () => TopicAssignmentPeriodValidator.ValidateExceptionOrdinal(AcademicYearDivision.None, 3);

        act.Should().Throw<TopicAssignmentPeriodException>()
            .Which.Message.Should().Contain("only when its division names a real part");
    }

    [TestMethod]
    public void ValidateExceptionOrdinal_UndefinedDivisionWithAnOrdinal_IsRejectedByTheDivisionRule()
    {
        // Order matters at the call site: ValidateExceptionDivision runs first, so an undefined
        // enum (42) is rejected as a division rather than slipping through this rule's
        // None-only check. Pinned here so the two guards cannot be reordered silently.
        Action undefinedDivision = () => TopicAssignmentPeriodValidator
            .ValidateExceptionDivision((AcademicYearDivision)42);
        Action ordinalOnAnUndefinedDivision = () => TopicAssignmentPeriodValidator
            .ValidateExceptionOrdinal((AcademicYearDivision)42, 1);

        undefinedDivision.Should().Throw<TopicAssignmentPeriodException>();
        ordinalOnAnUndefinedDivision.Should().NotThrow(
            "this rule only knows about None — the division guard is what rejects 42");
    }
}
