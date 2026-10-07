using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.LinkAssignmentGroups;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// P2-c (§17 of <c>documents/specs/assignment-authoring-compartments.md</c>): the explicit EF Core
/// transaction <see cref="LinkAssignmentGroupsHandler"/> wraps around its two writes — the target
/// replacement on the assignment aggregate (<c>IAssignmentRepository.UpdateAsync</c>) and the legacy
/// link-table replace (<see cref="IAssignmentActivityGroupRepository.ReplaceForAssignmentAsync"/>) —
/// really does roll the FIRST write back when the second faults. R2 shipped the transaction and verified
/// it by inspection only; the unit fixture runs InMemory with the transaction ignored, so nothing
/// exercised the rollback. This test does, against real Postgres: it runs the real handler over the real
/// <see cref="AssignmentsDbContext"/> with an open <c>DbTransaction</c>.
///
/// <para><b>Why it cannot live in the unit suite.</b> The rollback is a property of the Postgres
/// transaction, not of the C# code around it: an in-memory provider (or a mocked
/// <c>Database</c>) would satisfy every assertion below without ever opening a transaction.</para>
///
/// <para><b>Discrimination (verified, see the report).</b> Deleting
/// <c>BeginTransactionAsync</c>/<c>CommitAsync</c> from the handler makes the second write's failure
/// leave the FIRST write committed — the assignment's own row moves (<c>TargetAudienceType</c> to
/// <c>SelectedGroups</c>, <c>UpdatedAt</c> stamped) and the group target row the handler added survives
/// — and the assertion on the pre-handler state catches that as a failure. A rollback test that passes
/// with the transaction removed proves nothing, so this one was run both ways.</para>
/// </summary>
[TestClass]
public sealed class LinkAssignmentGroupsRollbackPostgresTests
{
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid StreamCodedValueId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid LinkedGroupId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    /// <summary>
    /// The second write faults with the first already executed inside the handler's transaction, so the
    /// three things P2-c was missing are asserted from a FRESH context — the only reader that can show
    /// what actually committed:
    /// <list type="number">
    /// <item>(i) the assignment's own row <b>and</b> its target rows equal the pre-handler state exactly,
    /// i.e. the first write rolled back;</item>
    /// <item>(ii) no link row exists — the two halves of the replace-set are still consistent with each
    /// other, which is the invariant the transaction exists for;</item>
    /// <item>(iii) the fault reaches the caller — the handler does not swallow it and leave the caller
    /// believing the link set was replaced.</item>
    /// </list>
    /// </summary>
    [TestMethod]
    public async Task SecondWriteFault_RollsBackTheFirstWrite_OnRealPostgres()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));

        // The seeded set is non-empty on purpose, and it is a STREAM target: the handler preserves the
        // non-group targets, so its SetTargets then moves the assignment ROW itself — the D-1 derived
        // compat column leaves Mixed for SelectedGroups (a grade target would NOT, since the derivation
        // prefers GradeLevel) — as well as stamping UpdatedAt. "The first write rolled back" is therefore
        // visible on the assignment row, not only on its child rows.
        Guid assignmentId;
        await using (var seed = AssignmentsDbFactory.CreateContext(connectionString, tenantId: TenantId))
        {
            var assignment = Assignment.Create(
                "Math — rollback fixture", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
                TargetAudienceType.AllStudents, TopicId, null, null, TeacherId)
                .WithTenant(TenantId);
            assignment.SetTargets([(TargetKind.Stream, (Guid?)StreamCodedValueId)], TenantId);
            assignmentId = assignment.Id;

            seed.Assignments.Add(assignment);
            await seed.SaveChangesAsync();
        }

        var before = await ReadPersistedStateAsync(connectionString, assignmentId);
        before.Audience.Should().Be(TargetAudienceType.Mixed,
            "fixture precondition: the lone stream target derives the compat column to Mixed, NOT to the " +
            "SelectedGroups the handler's group target will derive");
        before.TargetRows.Should().Contain($"Stream:{StreamCodedValueId}:0",
            "fixture precondition: the assignment starts with the one target the handler preserves");
        before.LinkRows.Should().Be(0, "fixture precondition: the legacy link table starts empty");

        var fault = new InvalidOperationException("fault injected: the link-table replace failed");
        var probe = new SecondWriteProbe();

        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, tenantId: TenantId))
        {
            // The real context, the real repositories and therefore the real transaction: only the
            // handler's SECOND write is faulted, and it faults after the first has run.
            var handler = new LinkAssignmentGroupsHandler(
                db,
                new AssignmentRepository(db),
                new SecondWriteFaultingLinkRepository(new AssignmentActivityGroupRepository(db), db, fault, probe),
                new StubActivityGroupLookup(LinkedGroupId),
                TenantFor(TenantId),
                new StubHybridCache(),
                NullLogger<LinkAssignmentGroupsHandler>.Instance);

            var thrown = await FluentActions
                .Awaiting(() => handler.HandleAsync(new LinkAssignmentGroups(assignmentId, [LinkedGroupId])))
                .Should().ThrowAsync<InvalidOperationException>(
                    "the link write's failure must reach the caller — swallowing it would report a " +
                    "replace-set that was never written");

            thrown.Which.Should().BeSameAs(fault, "the handler surfaces the failure itself, unwrapped");
        }

        // (iii)-adjacent, and the guard against a vacuous fixture: the fault really was injected AFTER the
        // first write had already executed inside the transaction — its target row was readable on the
        // handler's own connection at fault time. Without that, "the first write rolled back" would be
        // asserting that nothing happened at all.
        probe.TargetRowsVisibleAtFault.Should().BeTrue(
            "the fault has to land after the first write, inside the same open transaction");

        // (i)+(ii) from a FRESH context, after the handler's context (and with it the transaction) is gone.
        var after = await ReadPersistedStateAsync(connectionString, assignmentId);
        after.Should().Be(before,
            "the transaction's rollback is what the two writes' atomicity rests on: the committed " +
            "assignment row, its target rows and its link rows must all be exactly what they were before " +
            "the handler ran");
        after.LinkRows.Should().Be(0, "the faulted replace-set wrote no link row");
    }

    /// <summary>The assignment's persisted authoring state, read through a context of its own. Record
    /// equality is the assertion, so every column the handler's first write can touch is in here.</summary>
    private sealed record PersistedState(
        TargetAudienceType Audience,
        DateTimeOffset UpdatedAt,
        string TargetRows,
        int LinkRows);

    private static async Task<PersistedState> ReadPersistedStateAsync(string connectionString, Guid assignmentId)
    {
        await using var read = AssignmentsDbFactory.CreateContext(connectionString, tenantId: TenantId);

        // TargetAudienceType + UpdatedAt are the two assignment-row columns SetTargets writes (the D-1
        // derived compat column and the timestamp), and the target rows are what it replaces.
        var row = await read.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId);
        var targets = await read.AssignmentTargets.AsNoTracking()
            .Where(t => t.AssignmentId == assignmentId)
            .OrderBy(t => t.DisplayOrder)
            .Select(t => new { t.Kind, t.RefId, t.DisplayOrder })
            .ToArrayAsync();
        var links = await read.AssignmentActivityGroups.AsNoTracking()
            .CountAsync(l => l.AssignmentId == assignmentId);

        return new PersistedState(
            row.TargetAudienceType,
            row.UpdatedAt,
            string.Join(" | ", targets.Select(t => $"{t.Kind}:{t.RefId}:{t.DisplayOrder}")),
            links);
    }

    /// <summary>What the faulting second write observes at fault time.</summary>
    private sealed class SecondWriteProbe
    {
        public bool TargetRowsVisibleAtFault { get; set; }
    }

    /// <summary>
    /// The real <see cref="AssignmentActivityGroupRepository"/> with only
    /// <c>ReplaceForAssignmentAsync</c> — the handler's second write, and the one that follows the
    /// target write inside the transaction — replaced by a fault. Its reads still delegate, and, more
    /// importantly, it shares the handler's <see cref="AssignmentsDbContext"/>: the probe below reads on
    /// that context's own connection, so it sees exactly what the transaction has written so far.
    /// </summary>
    private sealed class SecondWriteFaultingLinkRepository(
        IAssignmentActivityGroupRepository inner,
        AssignmentsDbContext db,
        Exception fault,
        SecondWriteProbe probe) : IAssignmentActivityGroupRepository
    {
        public async Task ReplaceForAssignmentAsync(
            Guid assignmentId, Guid tenantId, IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default)
        {
            // The order matters more than the exception: the handler's first write (UpdateAsync) has
            // already run, so its group target row is visible here through the same open transaction.
            probe.TargetRowsVisibleAtFault = await db.AssignmentTargets.AsNoTracking()
                .AnyAsync(t => t.AssignmentId == assignmentId
                    && t.Kind == TargetKind.ActivityGroup
                    && t.RefId == LinkedGroupId, ct);

            throw fault;
        }

        public Task<Guid[]> GetGroupIdsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) =>
            inner.GetGroupIdsForAssignmentAsync(assignmentId, ct);

        public Task<Guid[]> GetAssignmentIdsByGroupAsync(Guid activityGroupId, CancellationToken ct = default) =>
            inner.GetAssignmentIdsByGroupAsync(activityGroupId, ct);

        public Task<AssignmentGroupSummaryDto[]> GetAssignmentsByGroupAsync(
            Guid activityGroupId, CancellationToken ct = default) =>
            inner.GetAssignmentsByGroupAsync(activityGroupId, ct);
    }

    /// <summary>The group port, answering the linked group as active in the caller's tenant. It is not
    /// part of what this test proves (the transaction is), so a stub keeps the fixture to the DB path
    /// under test.</summary>
    private sealed class StubActivityGroupLookup(Guid groupId) : IActivityGroupLookup
    {
        public Task<ActivityGroupRefDto[]> GetByIdsAsync(
            IReadOnlyList<Guid> activityGroupIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(activityGroupIds
                .Where(id => id == groupId)
                .Select(id => new ActivityGroupRefDto(id, "Chess", IsActive: true))
                .ToArray());

        public Task<Guid[]> GetActiveMemberIdsAsync(
            IReadOnlyList<Guid> activityGroupIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<Guid>());
    }

    /// <summary>The tenant the handler resolves — the same one the fixture's rows belong to, so the
    /// FR-6 save guards accept the writes.</summary>
    private static TenantProvider TenantFor(Guid tenantId)
    {
        var tenants = new TenantProvider();
        tenants.SetTenant(new TenantContext(tenantId, "TestTenant", TenantType.School));
        return tenants;
    }

    /// <summary>The handler invalidates the <c>assignments</c> cache tag only after a commit, which this
    /// test never reaches; the stub keeps the fixture on the DB path.</summary>
    private sealed class StubHybridCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => factory(state, cancellationToken);

        public override ValueTask SetAsync<T>(
            string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
