using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// One sub-period per position per year per division (subject-period-exception-model.md v5 §0
/// decision 15) — the READABLE half of the rule, the half that makes the collision a 422 naming
/// the sibling holding the position instead of the filtered unique index's unhandled
/// <c>DbUpdateException</c> (a 500).
///
/// <para>Three properties are pinned here: a taken position on CREATE is rejected and the message
/// NAMES the sibling; a taken position on UPDATE is rejected too; and the row being edited is
/// EXCLUDED from the check, so a sub-period can save the position it already holds without moving
/// itself. The index that enforces the same rule against races is
/// <c>ix_periods_tenant_parent_division_sequence</c> (migration
/// <c>20260928174930_AddPeriodSequenceAndExceptionOrdinal</c>), exercised through the API in the
/// Students integration suite.</para>
/// </summary>
[TestClass]
public class PeriodSequenceTakenTests
{
    private static CreatePeriodHandler NewCreate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, s.Tenants, NullLogger<CreatePeriodHandler>.Instance);

    private static UpdatePeriodHandler NewUpdate(StudentsTestScope s) =>
        new(s.Periods, s.Cache, NullLogger<UpdatePeriodHandler>.Instance);

    /// <summary>A Terms year covering 2027, created through the handler so every invariant
    /// the create path enforces still holds.</summary>
    private static async Task<Guid> SeedYearAsync(StudentsTestScope s) =>
        (await NewCreate(s).HandleAsync(new CreatePeriod(
            "AY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
            AcademicYearDivision.Terms))).YearId;

    private static Task<Guid> SeedTermAsync(StudentsTestScope s, Guid yearId, string name, int month, int? sequence) =>
        NewCreate(s).HandleAsync(new CreatePeriod(
            name, new DateOnly(2027, month, 1), new DateOnly(2027, month + 1, 28),
            AcademicYearDivision.Terms, ParentPeriodId: yearId, Sequence: sequence))
            .ContinueWith(t => t.Result.YearId);

    [TestMethod]
    public async Task Create_SubPeriod_OnATakenPosition_ThrowsNamingTheSibling()
    {
        using var s = new StudentsTestScope("seq-taken-create");
        var yearId = await SeedYearAsync(s);
        await SeedTermAsync(s, yearId, "Term 1", month: 1, sequence: 1);

        var act = async () => await SeedTermAsync(s, yearId, "Other term", month: 3, sequence: 1);

        var thrown = await act.Should().ThrowAsync<PeriodSequenceTakenException>(
            "a position a sibling already holds is the rule the filtered unique index enforces; without this "
            + "pre-check the collision surfaces as an unhandled DbUpdateException (a 500)");

        thrown.And.Message.Should().Contain("Term 1",
            "the caller can only act on the rejection if it says WHICH sibling holds the position");
        thrown.And.Message.Should().Contain("Terms", "and which run of positions it is talking about");
        thrown.And.Division.Should().Be(AcademicYearDivision.Terms);
        thrown.And.Sequence.Should().Be(1);
        thrown.And.ParentPeriodId.Should().Be(yearId);

        (await s.Db.Periods.CountAsync(p => p.ParentPeriodId == yearId)).Should().Be(1,
            "nothing was persisted: the check runs before any write");
    }

    [TestMethod]
    public async Task Create_SubPeriod_OnAFreePosition_Succeeds()
    {
        using var s = new StudentsTestScope("seq-free-create");
        var yearId = await SeedYearAsync(s);
        await SeedTermAsync(s, yearId, "Term 1", month: 1, sequence: 1);

        await SeedTermAsync(s, yearId, "Term 2", month: 3, sequence: 2);

        var terms = await s.Db.Periods.Where(p => p.ParentPeriodId == yearId).ToListAsync();
        terms.Should().HaveCount(2);
        terms.Select(t => t.Sequence).Should().BeEquivalentTo(new int?[] { 1, 2 },
            "the same position in a DIFFERENT division, or a free one, is never a collision");
    }

    [TestMethod]
    public async Task Create_SubPeriod_Unpositioned_IsAlwaysAllowed()
    {
        using var s = new StudentsTestScope("seq-null-create");
        var yearId = await SeedYearAsync(s);
        await SeedTermAsync(s, yearId, "Term 1", month: 1, sequence: 1);

        await SeedTermAsync(s, yearId, "Winter", month: 3, sequence: null);

        (await s.Db.Periods.SingleAsync(p => p.Name == "Winter")).Sequence.Should().BeNull(
            "NULL means \"not positioned\", and NULLs never collide — the index is filtered on sequence IS NOT NULL");
    }

    [TestMethod]
    public async Task Update_SubPeriod_KeepingItsOwnPosition_Succeeds()
    {
        using var s = new StudentsTestScope("seq-own-update");
        var yearId = await SeedYearAsync(s);
        var termId = await SeedTermAsync(s, yearId, "Term 1", month: 1, sequence: 1);
        await SeedTermAsync(s, yearId, "Term 2", month: 4, sequence: 2);

        // The row is EXCLUDED from its own check, so an edit that leaves the position alone is
        // not a collision with itself — without that exclusion the form could never be saved.
        await NewUpdate(s).HandleAsync(new UpdatePeriod(
            termId, "Term 1 (renamed)", new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 28),
            ParentPeriodId: yearId, Sequence: 1));

        (await s.Db.Periods.SingleAsync(p => p.Id == termId)).Sequence.Should().Be(1);
    }

    [TestMethod]
    public async Task Update_SubPeriod_OntoASiblingsPosition_ThrowsNamingTheSibling()
    {
        using var s = new StudentsTestScope("seq-taken-update");
        var yearId = await SeedYearAsync(s);
        var termId = await SeedTermAsync(s, yearId, "Term 1", month: 1, sequence: 1);
        await SeedTermAsync(s, yearId, "Term 2", month: 4, sequence: 2);

        var act = async () => await NewUpdate(s).HandleAsync(new UpdatePeriod(
            termId, "Term 1", new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 28),
            ParentPeriodId: yearId, Sequence: 2));

        var thrown = await act.Should().ThrowAsync<PeriodSequenceTakenException>();
        thrown.And.Message.Should().Contain("Term 2", "the sibling holding position 2 is named");
        thrown.And.Sequence.Should().Be(2);

        (await s.Db.Periods.SingleAsync(p => p.Id == termId)).Sequence.Should().Be(1,
            "the rejected update changed nothing — the period keeps the position it had");
    }
}
