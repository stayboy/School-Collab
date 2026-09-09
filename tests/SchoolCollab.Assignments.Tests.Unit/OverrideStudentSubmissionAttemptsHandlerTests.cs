using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.OverrideStudentSubmissionAttempts;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-A3 (spec §7 Q4) — <see cref="OverrideStudentSubmissionAttemptsCommandHandler"/>
/// stamps the override columns on a submission and removes the
/// "assignments" cache tag. Mirrors the
/// <c>EnableStudentSubmissionCommandHandler</c> shape.
/// </summary>
[TestClass]
public class OverrideStudentSubmissionAttemptsHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AssignmentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid GuardianId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private sealed class FakeHybridCache : HybridCache
    {
        public List<string> RemovedTags { get; } = new();
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => factory(state, cancellationToken);
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            RemovedTags.Add(tag);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSubmissionRepository : ISubmissionRepository
    {
        public AssignmentSubmission? SubmissionToReturn;
        public List<AssignmentSubmission> UpdatedSubmissions { get; } = new();
        public Task<AssignmentRecipient?> GetRecipientAsync(Guid a, Guid c, CancellationToken ct = default) => Task.FromResult<AssignmentRecipient?>(null);
        public void Add(AssignmentRecipient r) { }
        public void Update(AssignmentRecipient r) { }
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public void Add(GuardianSubmissionGate g) { }
        public void Update(GuardianSubmissionGate g) { }
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(new List<GuardianSubmissionGate>());
        public Task<AssignmentSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) => Task.FromResult(SubmissionToReturn);
        public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<AssignmentSubmission?>(null);
        public void Add(AssignmentSubmission s) { }
        public void Update(AssignmentSubmission s) => UpdatedSubmissions.Add(s);
        public void Add(AssignmentSubmissionVersion v) { }
        public void Add(SubmissionReview r) { }
        public void Add(SubmissionAnswer a) { }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid t, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianGateDto?>(null);
    }

    private static AssignmentSubmission ExistingSubmission() =>
        AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);

    [TestMethod]
    public async Task Override_StampsOverrideColumns_AndUpdatesCache()
    {
        var submission = ExistingSubmission();
        var subRepo = new FakeSubmissionRepository { SubmissionToReturn = submission };
        var cache = new FakeHybridCache();
        var teacherId = Guid.NewGuid();
        var handler = new OverrideStudentSubmissionAttemptsCommandHandler(
            subRepo, cache, NullLogger<OverrideStudentSubmissionAttemptsCommandHandler>.Instance);

        await handler.HandleAsync(new OverrideStudentSubmissionAttemptsCommand(submission.Id, teacherId));

        subRepo.UpdatedSubmissions.Should().ContainSingle(s => s.Id == submission.Id);
        var updated = subRepo.UpdatedSubmissions[0];
        updated.AttemptLimitOverriddenAt.Should().NotBeNull();
        updated.AttemptLimitOverriddenAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        updated.AttemptLimitOverriddenBy.Should().Be(teacherId);
        cache.RemovedTags.Should().ContainSingle().Which.Should().Be("assignments");
    }

    [TestMethod]
    public async Task Override_UnknownSubmission_Throws()
    {
        var subRepo = new FakeSubmissionRepository { SubmissionToReturn = null };
        var cache = new FakeHybridCache();
        var handler = new OverrideStudentSubmissionAttemptsCommandHandler(
            subRepo, cache, NullLogger<OverrideStudentSubmissionAttemptsCommandHandler>.Instance);

        var act = async () => await handler.HandleAsync(new OverrideStudentSubmissionAttemptsCommand(Guid.NewGuid(), Guid.NewGuid()));

        await act.Should().ThrowAsync<SubmissionNotFoundException>();
        subRepo.UpdatedSubmissions.Should().BeEmpty();
        cache.RemovedTags.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Override_EmptyTeacherId_Throws()
    {
        var submission = ExistingSubmission();
        var subRepo = new FakeSubmissionRepository { SubmissionToReturn = submission };
        var cache = new FakeHybridCache();
        var handler = new OverrideStudentSubmissionAttemptsCommandHandler(
            subRepo, cache, NullLogger<OverrideStudentSubmissionAttemptsCommandHandler>.Instance);

        var act = async () => await handler.HandleAsync(new OverrideStudentSubmissionAttemptsCommand(submission.Id, Guid.Empty));

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("teacherId");
        subRepo.UpdatedSubmissions.Should().BeEmpty();
    }
}
