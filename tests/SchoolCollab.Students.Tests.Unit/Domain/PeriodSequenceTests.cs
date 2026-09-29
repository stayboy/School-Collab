using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit.Domain;

/// <summary>
/// <see cref="Period.Sequence"/> invariants (subject-period-exception-model.md v5 §0
/// decision 15) — the position a sub-period holds in its academic year's run of the same
/// division. Two rules, both about the position being a <b>sub-period</b> concept:
/// it is 1-based (0 and negatives are not positions), and only a sub-period can carry one
/// (a top-level academic year is not the "1st" of anything).
///
/// <para>These are the ENTITY half. The storage half — one position per
/// <c>(tenant, parent_year, division)</c> — is the filtered unique index, and the readable
/// half is <c>PeriodSequenceGuard</c> in the create/update handlers (a 422 naming the
/// sibling that holds the position); both are covered elsewhere.</para>
/// </summary>
[TestClass]
public class PeriodSequenceTests
{
    private static readonly Guid ParentYearId = Guid.Parse("0e000000-0000-0000-0000-000000000001");

    private static Period NewSubPeriod(int? sequence) => Period.Create(
        "Term 1",
        new DateOnly(2027, 1, 1),
        new DateOnly(2027, 6, 30),
        AcademicYearDivision.Terms,
        parentPeriodId: ParentYearId,
        sequence: sequence);

    private static Period NewAcademicYear(int? sequence) => Period.Create(
        "2027 Academic Year",
        new DateOnly(2027, 1, 1),
        new DateOnly(2027, 12, 31),
        AcademicYearDivision.None,
        parentPeriodId: null,
        sequence: sequence);

    [TestMethod]
    public void Create_SubPeriod_WithPosition_StoresIt()
    {
        NewSubPeriod(3).Sequence.Should().Be(3,
            "the position IS the tenant's answer to \"which term is this?\" — it is stored, never inferred");
    }

    [TestMethod]
    public void Create_SubPeriod_WithoutPosition_IsLegal()
    {
        NewSubPeriod(null).Sequence.Should().BeNull(
            "an unpositioned sub-period is legal: the position is declared, not derived (Q8a backfills what it can)");
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Create_WithNonPositivePosition_Throws(int sequence)
    {
        var act = () => NewSubPeriod(sequence);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("sequence")
            .And.Message.Should().Contain("1 or greater",
                "positions are 1-based — 0 is not \"before the first term\", it is not a position");
    }

    [TestMethod]
    public void Create_TopLevelYear_WithPosition_Throws()
    {
        var act = () => NewAcademicYear(1);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("sequence")
            .And.Message.Should().Contain("Only a sub-period",
                "a top-level academic year is not the 1st of anything, and the filtered index keys on the parent year");
    }

    [TestMethod]
    public void Update_SubPeriod_PositionIsReplaceable()
    {
        var subPeriod = NewSubPeriod(1);

        subPeriod.Update(
            "Term 1", new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30),
            parentPeriodId: ParentYearId, sequence: 2);

        subPeriod.Sequence.Should().Be(2, "an existing sub-period must be able to move position (or take one at last)");
    }

    [TestMethod]
    public void Update_SubPeriod_NullPosition_ClearsIt()
    {
        var subPeriod = NewSubPeriod(1);

        subPeriod.Update(
            "Term 1", new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30),
            parentPeriodId: ParentYearId, sequence: null);

        subPeriod.Sequence.Should().BeNull(
            "Update is a FULL replace like every other mutable field, which is why every caller that edits a "
            + "positioned sub-period has to echo the value it read back");
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(-5)]
    public void Update_WithNonPositivePosition_Throws(int sequence)
    {
        var subPeriod = NewSubPeriod(1);

        var act = () => subPeriod.Update(
            "Term 1", new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30),
            parentPeriodId: ParentYearId, sequence: sequence);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("sequence")
            .And.Message.Should().Contain("1 or greater");
    }

    [TestMethod]
    public void Update_TopLevelYear_WithPosition_Throws()
    {
        var year = NewAcademicYear(null);

        var act = () => year.Update(
            "2027 Academic Year", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
            parentPeriodId: null, sequence: 1);

        act.Should().Throw<ArgumentException>().WithParameterName("sequence");
    }
}
