using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.SignOff;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-C1 — <see cref="ListSignOffStatusesQueryHandler"/> projection: per-ward
/// rows incl. recipient delivered/opened, signer/expected-singer names (with
/// id-degradation on lookup miss), current version score/result, and the
/// defensive empty list for non-signature assignments.
/// </summary>
[TestClass]
public class ListSignOffStatusesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000007e1");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000007e2");
    private static readonly Guid StudentA = Guid.Parse("00000000-0000-0000-0000-0000000007e3");
    private static readonly Guid StudentB = Guid.Parse("00000000-0000-0000-0000-0000000007e4");
    private static readonly Guid GuardianX = Guid.Parse("00000000-0000-0000-0000-0000000007e5");
    private static readonly Guid GuardianY = Guid.Parse("00000000-0000-0000-0000-0000000007e6");

    private sealed class FakeAssignmentRepository(Assignment? assignment) : IAssignmentRepository
    {
        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(assignment);
        public Task AddAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? s, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSummary>());
        public void DetectChanges() { }
        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSweepCandidate>());
    }

    private sealed class FakeSubmissionRepository : ISubmissionRepository
    {
        public List<AssignmentSubmission> Submissions { get; set; } = new();
        public List<AssignmentRecipient> Recipients { get; set; } = new();
        public List<SignatureEvent> Events { get; set; } = new();
        public List<AssignmentSubmissionVersion> Versions { get; set; } = new();

        public Task<AssignmentRecipient?> GetRecipientAsync(Guid a, Guid c, CancellationToken ct = default) => Task.FromResult<AssignmentRecipient?>(null);
        public void Add(AssignmentRecipient r) { }
        public void Update(AssignmentRecipient r) { }
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public void Add(GuardianSubmissionGate g) { }
        public void Update(GuardianSubmissionGate g) { }
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(new List<GuardianSubmissionGate>());
        public Task<AssignmentSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) => Task.FromResult<AssignmentSubmission?>(null);
        public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult(Submissions.FirstOrDefault(x => x.StudentId == s));
        public void Add(AssignmentSubmission s) { }
        public void Update(AssignmentSubmission s) { }
        public void Add(AssignmentSubmissionVersion v) { }
        public void Add(SubmissionReview r) { }
        public void Add(SubmissionAnswer a) { }
        public void Add(SignatureEvent e) => Events.Add(e);
        public Task<SignatureEvent?> GetSignatureEventByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult(Events.FirstOrDefault(e => e.StudentId == s));
        public Task<List<AssignmentSubmission>> ListSubmissionEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Submissions);
        public Task<List<AssignmentRecipient>> ListRecipientEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Recipients);
        public Task<List<AssignmentSubmissionVersion>> ListVersionsForSubmissionIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) => Task.FromResult(Versions.Where(v => ids.Contains(v.SubmissionId)).ToList());
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid t, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianGateDto?>(null);
    }

    private sealed class FakeStudentDirectory(Dictionary<Guid, string> names, Dictionary<Guid, List<WardGuardianInfo>> guardians) : IStudentDirectory
    {
        public Task<StudentNameInfo?> GetStudentNameAsync(Guid studentId, CancellationToken ct = default)
            => Task.FromResult(names.ContainsKey(studentId) ? new StudentNameInfo(studentId, names[studentId]) : null);
        public Task<IReadOnlyList<WardGuardianInfo>> GetGuardiansAsync(Guid studentId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WardGuardianInfo>>(guardians.TryGetValue(studentId, out var g) ? g : new List<WardGuardianInfo>());
        public Task<bool> IsGuardianOfAsync(Guid studentId, Guid guardianId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private static Assignment NewRequiresSignatureAssignment() =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId,
            mandatoryReview: false, requiresSignature: true);

    private static Assignment NewNoSignatureAssignment() =>
        Assignment.Create("Quiet", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId,
            mandatoryReview: false);

    private static AssignmentSubmission NewSubmission(Guid student, SignOffState state)
    {
        var s = AssignmentSubmission.Create(TenantId, AssignmentId, student, null);
        if (state == SignOffState.AwaitingSignature) s.MarkAwaitingSignature();
        if (state == SignOffState.Signed) { s.MarkAwaitingSignature(); s.MarkSigned(); }
        return s;
    }

    private static SignatureEvent NewEvent(Guid student, Guid guardian) =>
        SignatureEvent.Create(TenantId, AssignmentId, student, guardian, SignatureType.Click, null,
            "127.0.0.1", "TestAgent/1.0", "consent");

    private static AssignmentRecipient NewRecipient(Guid student, Guid contact, GuardianRole role, bool delivered)
    {
        var r = AssignmentRecipient.Create(TenantId, AssignmentId, ContactOwnerType.Student, student,
            student, contact, ContactChannel.Email, role, true, true);
        if (delivered)
        {
            r.MarkDelivered();
            r.MarkOpened();
        }
        return r;
    }

    [TestMethod]
    public async Task ReturnsEmpty_WhenRequiresSignatureFalse()
    {
        var handler = new ListSignOffStatusesQueryHandler(
            new FakeAssignmentRepository(NewNoSignatureAssignment()),
            new FakeSubmissionRepository(),
            new FakeStudentDirectory(new(), new()),
            NullLogger<ListSignOffStatusesQueryHandler>.Instance);

        var rows = await handler.HandleAsync(new ListSignOffStatusesQuery(AssignmentId), CancellationToken.None);

        rows.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Throws_WhenAssignmentMissing()
    {
        var handler = new ListSignOffStatusesQueryHandler(
            new FakeAssignmentRepository(null),
            new FakeSubmissionRepository(),
            new FakeStudentDirectory(new(), new()),
            NullLogger<ListSignOffStatusesQueryHandler>.Instance);

        var act = () => handler.HandleAsync(new ListSignOffStatusesQuery(AssignmentId), CancellationToken.None);

        await act.Should().ThrowAsync<AssignmentNotFoundException>();
    }

    [TestMethod]
    public async Task Projects_RecipientDeliveredOpened()
    {
        var subA = NewSubmission(StudentA, SignOffState.AwaitingSignature);
        var subB = NewSubmission(StudentB, SignOffState.None);
        var repo = new FakeSubmissionRepository
        {
            Submissions = new() { subA, subB },
            // A delivered+opened; B not delivered.
            Recipients = new()
            {
                NewRecipient(StudentA, GuardianX, GuardianRole.Primary, delivered: true),
                NewRecipient(StudentB, GuardianY, GuardianRole.CC, delivered: false),
            }
        };

        var handler = new ListSignOffStatusesQueryHandler(
            new FakeAssignmentRepository(NewRequiresSignatureAssignment()), repo,
            new FakeStudentDirectory(new(), new()),
            NullLogger<ListSignOffStatusesQueryHandler>.Instance);

        var rows = await handler.HandleAsync(new ListSignOffStatusesQuery(AssignmentId), CancellationToken.None);

        rows.Should().HaveCount(2);
        var rowA = rows.First(r => r.StudentId == StudentA);
        rowA.Delivered.Should().BeTrue();
        rowA.Opened.Should().BeTrue();
        var rowB = rows.First(r => r.StudentId == StudentB);
        rowB.Delivered.Should().BeFalse();
        rowB.Opened.Should().BeFalse();
    }

    [TestMethod]
    public async Task Projects_SignerAndNames()
    {
        var subA = NewSubmission(StudentA, SignOffState.Signed);
        var repo = new FakeSubmissionRepository
        {
            Submissions = new() { subA },
            Events = new() { NewEvent(StudentA, GuardianX) },
            Versions = new()
        };
        var directory = new FakeStudentDirectory(
            new() { [StudentA] = "Alice Ward" },
            new() { [StudentA] = new()
                {
                    new WardGuardianInfo(GuardianX, "Xavier Guardian", IsPrimary: true)
                } });

        var handler = new ListSignOffStatusesQueryHandler(
            new FakeAssignmentRepository(NewRequiresSignatureAssignment()), repo, directory,
            NullLogger<ListSignOffStatusesQueryHandler>.Instance);

        var rows = await handler.HandleAsync(new ListSignOffStatusesQuery(AssignmentId), CancellationToken.None);

        var rowA = rows.First(r => r.StudentId == StudentA);
        rowA.StudentName.Should().Be("Alice Ward");
        rowA.SignOffState.Should().Be(SignOffStateDto.Signed);
        rowA.SignerGuardianId.Should().Be(GuardianX);
        rowA.SignerName.Should().Be("Xavier Guardian");
    }

    [TestMethod]
    public async Task DegradesNamesToIds_WhenLookupFails()
    {
        var subA = NewSubmission(StudentA, SignOffState.AwaitingSignature);
        var repo = new FakeSubmissionRepository
        {
            Submissions = new() { subA },
            Events = new() { NewEvent(StudentA, GuardianX) },
        };
        var directory = new FakeStudentDirectory(new(), new());

        var handler = new ListSignOffStatusesQueryHandler(
            new FakeAssignmentRepository(NewRequiresSignatureAssignment()), repo, directory,
            NullLogger<ListSignOffStatusesQueryHandler>.Instance);

        var rows = await handler.HandleAsync(new ListSignOffStatusesQuery(AssignmentId), CancellationToken.None);

        var rowA = rows.First(r => r.StudentId == StudentA);
        rowA.StudentName.Should().Be(StudentA.ToString()); // degraded to id
        rowA.SignerName.Should().Be(GuardianX.ToString()); // degraded to id
    }
}
