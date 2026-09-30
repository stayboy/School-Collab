using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.GradeStreams.Commands.SetGradeStreamOrder;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// AC1 (swap semantics on the bridge row's <c>DisplayOrder</c>), AC3 (duplicate
/// orders normalised before the swap), AC4 (out-of-range → throws), AC5
/// (assignment not in that grade / other tenant → throws) and AC6 (row-version
/// conflict → <see cref="ConcurrencyException"/>) for the streams reorder
/// command.
/// </summary>
/// <remarks>
/// The scope is built by each TEST METHOD, never inside a helper: the tenant
/// provider keeps its tenant in an <see cref="System.Threading.AsyncLocal{T}"/>,
/// and a scope constructed in a called async method does not reliably carry that
/// tenant back to the caller.
/// </remarks>
[TestClass]
public class SetGradeStreamOrderHandlerTests
{
    private static readonly Guid OtherTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static SetGradeStreamOrderHandler NewHandler(StudentsTestScope s) =>
        new(s.GradeStreamAssignments, s.Cache, NullLogger<SetGradeStreamOrderHandler>.Instance);

    private static async Task<Guid> SeedGradeAsync(StudentsTestScope scope, string name = "Grade 5")
    {
        var grade = GradeLevel.Create(Guid.NewGuid(), level: 5, name: name, displayOrder: 5);
        scope.Db.GradeLevels.Add(grade);
        await scope.Db.SaveChangesAsync();
        return grade.Id;
    }

    /// <summary>Seeds one bridge row per entry and returns stream id → bridge row id.</summary>
    private static async Task<Dictionary<Guid, Guid>> SeedStreamsAsync(
        StudentsTestScope s, Guid gradeId, params (int Order, Guid StreamId)[] streams)
    {
        var rows = new Dictionary<Guid, Guid>();
        foreach (var (order, streamId) in streams)
        {
            var row = await s.GradeStreamAssignments.AddOrReuseAsync(
                GradeStreamAssignment.Create(gradeId, streamId, order));
            rows[streamId] = row.Id;
        }
        return rows;
    }

    private static async Task<Dictionary<Guid, int>> OrdersByAssignmentAsync(
        StudentsTestScope s, Guid gradeId) =>
        await s.Db.GradeStreamAssignments
            .Where(x => x.GradeLevelId == gradeId)
            .ToDictionaryAsync(x => x.Id, x => x.DisplayOrder);

    // ── AC1: swap ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Swap_MovesMoverToTargetPosition_DisplacedTakesMoverOldOrder()
    {
        using var s = new StudentsTestScope("stgs-swap");
        var gradeId = await SeedGradeAsync(s);
        var firstStream = Guid.NewGuid();
        var middleStream = Guid.NewGuid();
        var lastStream = Guid.NewGuid();
        var rows = await SeedStreamsAsync(s, gradeId,
            (0, firstStream), (1, middleStream), (2, lastStream));

        // Move the LAST stream to position 0 (the dialog's move-up path).
        await NewHandler(s).HandleAsync(new SetGradeStreamOrder(gradeId, rows[lastStream], 0));

        var orders = await OrdersByAssignmentAsync(s, gradeId);
        orders[rows[lastStream]].Should().Be(0, "the mover takes the requested position");
        orders[rows[firstStream]].Should().Be(2, "the displaced stream takes the mover's old position");
        orders[rows[middleStream]].Should().Be(1, "no other row's order changes");
    }

    // ── AC3: normalisation before the swap ──────────────────────────────────

    [TestMethod]
    public async Task DuplicateOrders_AreNormalisedBeforeTheSwap_AndEndContiguous()
    {
        // The migration backfill can leave duplicates (a hand-edited order, or a
        // pre-column DB). The handler must renumber first, or the swap would leave a
        // hole/duplicate.
        using var s = new StudentsTestScope("stgs-duplicates");
        var gradeId = await SeedGradeAsync(s);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var rows = await SeedStreamsAsync(s, gradeId, (0, a), (0, b), (0, c));

        await NewHandler(s).HandleAsync(new SetGradeStreamOrder(gradeId, rows[b], 2));

        var orders = await OrdersByAssignmentAsync(s, gradeId);
        orders.Values.Should().BeEquivalentTo(
            new[] { 0, 1, 2 },
            "every duplicate order is normalised to a unique contiguous position");
        orders[rows[b]].Should().Be(2, "the mover lands at the requested position");
    }

    // ── AC4: out-of-range ───────────────────────────────────────────────────

    [TestMethod]
    public async Task OutOfRangeOrder_Throws_AndPersistsNothing()
    {
        using var s = new StudentsTestScope("stgs-out-of-range");
        var gradeId = await SeedGradeAsync(s);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var rows = await SeedStreamsAsync(s, gradeId, (0, a), (1, b));

        var tooHigh = () => NewHandler(s).HandleAsync(new SetGradeStreamOrder(gradeId, rows[a], 2));
        await tooHigh.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var negative = () => NewHandler(s).HandleAsync(new SetGradeStreamOrder(gradeId, rows[a], -1));
        await negative.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var orders = await OrdersByAssignmentAsync(s, gradeId);
        orders[rows[a]].Should().Be(0, "a rejected position must not be persisted");
        orders[rows[b]].Should().Be(1);
    }

    // ── AC5: not in that grade / other tenant ───────────────────────────────

    [TestMethod]
    public async Task AssignmentOfAnotherGrade_ThrowsNotFound()
    {
        using var s = new StudentsTestScope("stgs-other-grade");
        var gradeId = await SeedGradeAsync(s, "Grade 5");
        var otherGradeId = await SeedGradeAsync(s, "Grade 6");
        var rows = await SeedStreamsAsync(s, gradeId, (0, Guid.NewGuid()));

        var act = () => NewHandler(s).HandleAsync(
            new SetGradeStreamOrder(otherGradeId, rows.Values.Single(), 0));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not belong to grade level*");
    }

    [TestMethod]
    public async Task UnknownAssignmentId_ThrowsNotFound()
    {
        using var s = new StudentsTestScope("stgs-unknown-assignment");
        var gradeId = await SeedGradeAsync(s);
        await SeedStreamsAsync(s, gradeId, (0, Guid.NewGuid()));

        var act = () => NewHandler(s).HandleAsync(new SetGradeStreamOrder(gradeId, Guid.NewGuid(), 0));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task AnotherTenantsAssignment_IsInvisible_AndThrowsNotFound()
    {
        using var s = new StudentsTestScope("stgs-cross-tenant");
        var gradeId = await SeedGradeAsync(s);
        Guid foreignAssignmentId;

        using (s.TenantAccessor.SuppressTenantGuard())
        {
            var foreign = GradeStreamAssignment.Create(gradeId, Guid.NewGuid(), 0).WithTenant(OtherTenantId);
            s.Db.GradeStreamAssignments.Add(foreign);
            await s.Db.SaveChangesAsync();
            foreignAssignmentId = foreign.Id;
        }

        var act = () => NewHandler(s).HandleAsync(new SetGradeStreamOrder(gradeId, foreignAssignmentId, 0));

        await act.Should().ThrowAsync<InvalidOperationException>(
            "the load is tenant-filtered, so another tenant's row is not reorderable");
    }

    // ── AC6: row-version conflict ───────────────────────────────────────────

    [TestMethod]
    public async Task RowVersionConflict_ThrowsConcurrencyException()
    {
        using var s = new StudentsTestScope("stgs-conflict");
        var gradeId = Guid.NewGuid();
        var rows = new[]
        {
            GradeStreamAssignment.Create(gradeId, Guid.NewGuid(), 0),
            GradeStreamAssignment.Create(gradeId, Guid.NewGuid(), 1),
        };

        // The in-memory provider has no xmin, so the conflict is raised by the save
        // seam itself — that is the contract the endpoint's 409 catch depends on.
        var repository = new Mock<IGradeStreamAssignmentRepository>();
        repository
            .Setup(r => r.ListByGradeLevelForUpdateAsync(gradeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        repository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("row version mismatch"));

        var handler = new SetGradeStreamOrderHandler(
            repository.Object, s.Cache, NullLogger<SetGradeStreamOrderHandler>.Instance);

        var act = () => handler.HandleAsync(new SetGradeStreamOrder(gradeId, rows[0].Id, 1));

        await act.Should().ThrowAsync<ConcurrencyException>();
    }
}
