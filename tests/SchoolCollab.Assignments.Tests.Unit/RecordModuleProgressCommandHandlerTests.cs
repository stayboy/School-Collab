using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RecordModuleProgress;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-D1 / decision (d) — <see cref="RecordModuleProgressCommandHandler"/>
/// upserts the per-ward progress row keyed by (assignment, student, module):
/// first report creates it, later reports reconcile monotonically (no second
/// row, no regression), an unknown module returns false (route 404) and a
/// missing assignment throws. The row is stamped with the tenant context.
/// </summary>
[TestClass]
public class RecordModuleProgressCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AssignmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ModuleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private sealed class FakeTenantProvider : ITenantProvider
    {
        private readonly TenantContext _ctx;
        public FakeTenantProvider(Guid tenantId) => _ctx = new TenantContext(tenantId, tenantId.ToString(), TenantType.School);
        public TenantContext GetTenantContext() => _ctx;
    }

    private sealed class FakeAssignmentRepository(Assignment? assignment) : IAssignmentRepository
    {
        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(assignment);
        public Task AddAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? s, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSummary>());
        public void DetectChanges() { }
        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset n, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset n, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSweepCandidate>());
    }

    private sealed class FakeModuleProgressRepository : IModuleProgressRepository
    {
        public List<ModuleProgress> Rows { get; } = new();
        public int SaveCalls;
        public Task<ModuleProgress?> GetAsync(Guid a, Guid s, Guid m, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(p => p.AssignmentId == a && p.StudentId == s && p.ContentModuleId == m));
        public Task<List<ModuleProgress>> ListProgressForAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default)
            => Task.FromResult(Rows.Where(p => p.AssignmentId == a && p.StudentId == s).ToList());
        public void Add(ModuleProgress p) => Rows.Add(p);
        public Task<List<AssignmentSummary>> ListWardAssignmentsAsync(Guid studentId, DateTimeOffset nowUtc, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSummary>());
        public Task<int> SaveChangesAsync(CancellationToken ct = default) { SaveCalls++; return Task.FromResult(1); }
    }

    private static Assignment NewAssignmentWithModule(int threshold = 80)
    {
        var a = Assignment.Create("Gated", null, AssignmentType.Digital,
            GradingFormat.TeacherGraded, TargetAudienceType.AllStudents,
            TopicId, null, null, null, TeacherId, mandatoryReview: false)
            .WithTenant(new FakeTenantProvider(TenantId));
        SetId(a, AssignmentId);
        var module = a.AddModule(ModuleType.Video, "https://video", title: "Req", minCompletionThresholdPercent: threshold, isRequired: true);
        SetId(module, ModuleId);
        return a;
    }

    private static void SetId(object entity, Guid id) =>
        entity.GetType().GetProperty("Id")!.SetValue(entity, id);

    private static RecordModuleProgressCommandHandler NewHandler(Assignment? assignment, FakeModuleProgressRepository moduleRepo)
        => new(
            new FakeAssignmentRepository(assignment),
            moduleRepo,
            new FakeTenantProvider(TenantId),
            NullLogger<RecordModuleProgressCommandHandler>.Instance);

    [TestMethod]
    public async Task FirstReport_UpsertsRow()
    {
        var moduleRepo = new FakeModuleProgressRepository();
        var handler = NewHandler(NewAssignmentWithModule(), moduleRepo);

        var ok = await handler.HandleAsync(new RecordModuleProgressCommand(AssignmentId, StudentId, ModuleId, 50));

        ok.Should().BeTrue();
        moduleRepo.Rows.Should().ContainSingle();
        var row = moduleRepo.Rows.Single();
        row.TenantId.Should().Be(TenantId, "the row is stamped with the tenant context");
        row.PercentComplete.Should().Be(50);
        row.CompletedAt.Should().BeNull();
        moduleRepo.SaveCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task Replay_LowerOrEqual_IsNoOp_NoSecondRow()
    {
        var moduleRepo = new FakeModuleProgressRepository();
        var handler = NewHandler(NewAssignmentWithModule(), moduleRepo);

        await handler.HandleAsync(new RecordModuleProgressCommand(AssignmentId, StudentId, ModuleId, 40));
        var first = moduleRepo.Rows.Single();
        await handler.HandleAsync(new RecordModuleProgressCommand(AssignmentId, StudentId, ModuleId, 30));

        moduleRepo.Rows.Should().ContainSingle("an equal/lower replay never adds a row");
        moduleRepo.Rows.Single().Should().BeSameAs(first, "upsert mutates the existing row, it is not replaced");
        moduleRepo.Rows.Single().PercentComplete.Should().Be(40, "progress never regresses");
    }

    [TestMethod]
    public async Task HigherReport_RaisesAndStampsCompletion()
    {
        var moduleRepo = new FakeModuleProgressRepository();
        var handler = NewHandler(NewAssignmentWithModule(threshold: 80), moduleRepo);

        await handler.HandleAsync(new RecordModuleProgressCommand(AssignmentId, StudentId, ModuleId, 70));
        await handler.HandleAsync(new RecordModuleProgressCommand(AssignmentId, StudentId, ModuleId, 90));

        var row = moduleRepo.Rows.Single();
        row.PercentComplete.Should().Be(90);
        row.CompletedAt.Should().NotBeNull("90 >= the 80 threshold stamps completion");
    }

    [TestMethod]
    public async Task ModuleNotOnAssignment_ReturnsFalse()
    {
        var moduleRepo = new FakeModuleProgressRepository();
        var assignment = NewAssignmentWithModule();
        SetId(assignment, AssignmentId);
        // A real (guid) module id that is NOT the assignment's module.
        var handler = NewHandler(assignment, moduleRepo);

        var ok = await handler.HandleAsync(
            new RecordModuleProgressCommand(AssignmentId, StudentId, Guid.NewGuid(), 50));

        ok.Should().BeFalse();
        moduleRepo.Rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AssignmentMissing_Throws()
    {
        var moduleRepo = new FakeModuleProgressRepository();
        var handler = NewHandler(assignment: null, moduleRepo);

        var act = () => handler.HandleAsync(new RecordModuleProgressCommand(AssignmentId, StudentId, ModuleId, 50));
        await act.Should().ThrowAsync<AssignmentNotFoundException>();
    }
}
