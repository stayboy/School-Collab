using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// The BOUNDARY half of the position invariants (subject-period-exception-model.md v5 §0
/// decision 15, P2-3 of the round's static review): a position that is not a position at all —
/// below 1, or carried by a top-level academic year — is rejected by the handlers as
/// <see cref="PeriodSequenceInvalidException"/>, which the period routes map to <b>422</b>.
///
/// <para>The entity still rejects both shapes (<c>Period.ValidateSequence</c>, covered by
/// <see cref="PeriodSequenceTests"/>), but it does so with <see cref="ArgumentException"/>, and
/// the routes map that to <b>400</b>. Without this boundary check the same round's position rules
/// would answer in two status families: a taken position a 422, an impossible one a 400. The
/// assertion that matters most here is therefore not only "it throws", but that it throws the
/// type the routes turn into a 422.</para>
/// </summary>
[TestClass]
public class PeriodSequenceBoundaryTests
{
    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    private static UpdatePeriodHandler NewUpdate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, NullLogger<UpdatePeriodHandler>.Instance);

    private static async Task<Guid> SeedYearAsync(StudentsTestScope s) =>
        (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
            AcademicYearDivision.Terms))).YearId;

    [TestMethod]
    public async Task Create_SubPeriod_WithAPositionBelowOne_IsABoundaryRejection()
    {
        using var s = new StudentsTestScope("seq-boundary-create-zero");
        var yearId = await SeedYearAsync(s);

        var act = async () => await NewCreate(s).HandleAsync(new CreatePeriod(
            "Zeroth term", new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 28),
            AcademicYearDivision.Terms, ParentPeriodId: yearId, Sequence: 0));

        var thrown = await act.Should().ThrowAsync<PeriodSequenceInvalidException>(
            "positions are 1-based, so 0 is not a position — and the API must answer 422 (this type) "
            + "rather than the 400 the entity's own ArgumentException would produce");
        thrown.And.Message.Should().Contain("1 or greater");
    }

    [TestMethod]
    public async Task Create_TopLevelYear_WithAPosition_IsABoundaryRejection()
    {
        using var s = new StudentsTestScope("seq-boundary-create-top");

        var act = async () => await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
            AcademicYearDivision.Terms, ParentPeriodId: null, Sequence: 2));

        var thrown = await act.Should().ThrowAsync<PeriodSequenceInvalidException>(
            "a year is not the 2nd term of anything; the position belongs to a sub-period");
        thrown.And.Message.Should().Contain("top-level academic year");
    }

    [TestMethod]
    public async Task Update_SubPeriod_WithAPositionBelowOne_IsABoundaryRejection()
    {
        using var s = new StudentsTestScope("seq-boundary-update-zero");
        var yearId = await SeedYearAsync(s);
        var termId = (await NewCreate(s).HandleAsync(new CreatePeriod(
            "Term 1", new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 28),
            AcademicYearDivision.Terms, ParentPeriodId: yearId, Sequence: 1))).YearId;

        var act = async () => await NewUpdate(s).HandleAsync(new UpdatePeriod(
            termId, "Term 1", new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 28),
            ParentPeriodId: yearId, Sequence: -1));

        await act.Should().ThrowAsync<PeriodSequenceInvalidException>(
            "an edit is validated at the same boundary as a create — the position rule is one rule");
    }

    [TestMethod]
    public async Task Create_SubPeriod_LeavingThePositionOut_StillSucceeds()
    {
        // The control. A boundary validator that rejected more than the two rules would be a
        // regression dressed as a fix: an unpositioned sub-period is legal (NULL means "not
        // positioned") and is what the atomic year+sub-periods create still produces.
        using var s = new StudentsTestScope("seq-boundary-control");
        var yearId = await SeedYearAsync(s);

        var created = await NewCreate(s).HandleAsync(new CreatePeriod(
            "Unpositioned term", new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 28),
            AcademicYearDivision.Terms, ParentPeriodId: yearId));

        (await s.Db.Periods.FindAsync(created.YearId))!.Sequence.Should().BeNull();
    }
}
