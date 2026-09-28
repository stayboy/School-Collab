using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit.Domain;

/// <summary>
/// Entity invariants for <see cref="SubjectEnrollmentException"/>
/// (subject-period-exception-model.md v3 §2.2). An exception is the period-part +
/// date-span declaration that a subject is NOT offered: exactly one owner, at least
/// one span bound, EndDate &gt;= StartDate when both are set, no period reference and
/// no status column.
/// </summary>
[TestClass]
public class SubjectEnrollmentExceptionTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GradeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid GroupId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid TopicId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [TestMethod]
    public void Create_WithGradeOwner_SetsOwnerDivisionSpanAndAuditStamps()
    {
        var exception = SubjectEnrollmentException.Create(
            TenantId, GradeId, null, TopicId, AcademicYearDivision.Terms, D(2027, 1, 1), D(2027, 3, 31));

        exception.Id.Should().NotBeEmpty();
        exception.TenantId.Should().Be(TenantId);
        exception.GradeLevelId.Should().Be(GradeId);
        exception.ActivityGroupId.Should().BeNull();
        exception.TopicId.Should().Be(TopicId);
        exception.Division.Should().Be(AcademicYearDivision.Terms);
        exception.StartDate.Should().Be(D(2027, 1, 1));
        exception.EndDate.Should().Be(D(2027, 3, 31));
        exception.Reason.Should().BeNull();
        exception.CreatedAt.Should().NotBe(default);
        exception.UpdatedAt.Should().NotBe(default);
        exception.IsDeleted.Should().BeFalse("a fresh exception is live");
    }

    [TestMethod]
    public void Create_WithGroupOwner_SetsGroupOwner()
    {
        var exception = SubjectEnrollmentException.Create(
            TenantId, null, GroupId, TopicId, AcademicYearDivision.None, D(2027, 1, 1));

        exception.ActivityGroupId.Should().Be(GroupId);
        exception.GradeLevelId.Should().BeNull();
    }

    [TestMethod]
    public void Create_WithBothOwners_Throws()
    {
        var act = () => SubjectEnrollmentException.Create(
            TenantId, GradeId, GroupId, TopicId, AcademicYearDivision.Terms, D(2027, 1, 1));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*exactly one owner*");
    }

    [TestMethod]
    public void Create_WithNeitherOwner_Throws()
    {
        var act = () => SubjectEnrollmentException.Create(
            TenantId, null, null, TopicId, AcademicYearDivision.Terms, D(2027, 1, 1));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*exactly one owner*");
    }

    [TestMethod]
    public void Create_WithNoBound_Throws_UnboundedWouldMeanNeverOffered()
    {
        // §0 decision 8: a span may be open at the start, open at the end, or both —
        // but not neither. "Never offered" is a different concept.
        var act = () => SubjectEnrollmentException.Create(
            TenantId, GradeId, null, TopicId, AcademicYearDivision.Terms);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least one bound*");
    }

    [TestMethod]
    public void Create_WithOpenStart_IsAllowed()
    {
        var exception = SubjectEnrollmentException.Create(
            TenantId, GradeId, null, TopicId, AcademicYearDivision.None, startDate: null, endDate: D(2027, 3, 31));

        exception.StartDate.Should().BeNull("an open start is a valid bound");
        exception.EndDate.Should().Be(D(2027, 3, 31));
    }

    [TestMethod]
    public void Create_WithOpenEnd_IsAllowed()
    {
        var exception = SubjectEnrollmentException.Create(
            TenantId, GradeId, null, TopicId, AcademicYearDivision.None, startDate: D(2027, 1, 1));

        exception.StartDate.Should().Be(D(2027, 1, 1));
        exception.EndDate.Should().BeNull("an open end is a valid bound");
    }

    [TestMethod]
    public void Create_WithEndBeforeStart_Throws()
    {
        var act = () => SubjectEnrollmentException.Create(
            TenantId, GradeId, null, TopicId, AcademicYearDivision.Terms, D(2027, 3, 31), D(2027, 1, 1));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*on or after*");
    }

    [TestMethod]
    public void Create_WithSingleDaySpan_IsAllowed()
    {
        // EndDate == StartDate is the inclusive one-day span, not an ordering error.
        var exception = SubjectEnrollmentException.Create(
            TenantId, GradeId, null, TopicId, AcademicYearDivision.None, D(2027, 3, 1), D(2027, 3, 1));

        exception.StartDate.Should().Be(exception.EndDate);
    }

    [TestMethod]
    public void Create_WithEmptyTopic_Throws()
    {
        var act = () => SubjectEnrollmentException.Create(
            TenantId, GradeId, null, Guid.Empty, AcademicYearDivision.Terms, D(2027, 1, 1));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*requires a topic*");
    }

    [TestMethod]
    public void Create_TrimsReason_AndNormalisesBlankToNull()
    {
        SubjectEnrollmentException.Create(
                TenantId, GradeId, null, TopicId, AcademicYearDivision.Terms, D(2027, 1, 1), null, "  teacher on leave  ")
            .Reason.Should().Be("teacher on leave");

        SubjectEnrollmentException.Create(
                TenantId, GradeId, null, TopicId, AcademicYearDivision.Terms, D(2027, 1, 1), null, "   ")
            .Reason.Should().BeNull();
    }

    [TestMethod]
    public void MarkAsDeleted_SoftDeletes_TheAuditBaseSuppliesTheFlag()
    {
        // There is deliberately no status column: removing an exception is a soft
        // delete, and the is_deleted column comes from BaseTenantEntityWithAudit.
        var exception = SubjectEnrollmentException.Create(
            TenantId, GradeId, null, TopicId, AcademicYearDivision.Terms, D(2027, 1, 1));
        var before = exception.UpdatedAt;

        exception.MarkAsDeleted();

        exception.IsDeleted.Should().BeTrue();
        exception.DeletedAt.Should().NotBeNull();
        exception.UpdatedAt.Should().BeOnOrAfter(before);
    }
}
