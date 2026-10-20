using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ApproveAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ArchiveAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CloseAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DeleteAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DuplicateAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.LinkAssignmentGroups;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionsDraft;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RegenerateAttachmentExtraction;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RejectAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.StageAttachmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentForApprovalCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UnpublishAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>assignment-approval-workflow</c> (spec §3.2, Q9) — the pending-approval edit hold, ONE
/// TEST PER ROW of the round plan's hold/exempt table. The table is the contract: a missed command
/// is a silent hole an approver cannot see, which is why every row is pinned individually rather
/// than by one "the hold works" test.
///
/// <para><b>Held rows</b> assert the typed <see cref="AssignmentPendingApprovalException"/>.
/// <b>Exempt rows</b> assert that the hold exception is NOT thrown and that the handler's
/// pre-existing behaviour applies — for Publish/Schedule that is
/// <see cref="AssignmentApprovalRequiredException"/>, for Unpublish/Archive their own status guard,
/// for Close its idempotent success.</para>
/// </summary>
[TestClass]
public class AssignmentPendingApprovalHoldTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ApproverId = Guid.Parse("00000000-0000-0000-0000-000000000099");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static Assignment NewAssignment()
    {
        var assignment = Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId)
            .WithTenant(TenantId);
        assignment.AddQuestion("Q1", QuestionType.ShortAnswer, 0, null);
        return assignment;
    }

    /// <summary>The row under review: submitted for approval and therefore Pending (Status stays Draft).</summary>
    private static Assignment Pending()
    {
        var assignment = NewAssignment();
        assignment.SubmitForApproval();
        return assignment;
    }

    private static HybridCache NewCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    private static Mock<IAssignmentRepository> Repo(Assignment assignment)
    {
        var repo = new Mock<IAssignmentRepository>();
        repo.Setup(r => r.GetAsync(assignment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(assignment);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Assignment>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.DeleteAsync(It.IsAny<Assignment>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repo;
    }

    private static (AssignmentsDbContext Db, ITenantProvider Tenants) InMemoryScope(string name)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<AssignmentsDbContext>();
        db.Database.EnsureCreated();
        var tenants = sp.GetRequiredService<ITenantProvider>();
        ((TenantProvider)tenants).SetTenant(new TenantContext(TenantId, "School", TenantType.School));
        return (db, tenants);
    }

    private static Task AssertHeldAsync(Func<Task> action) =>
        FluentActions.Awaiting(action).Should().ThrowAsync<AssignmentPendingApprovalException>();

    // ══ HOLD rows ═══════════════════════════════════════════════════════

    [TestMethod]
    public async Task Update_WhilePending_IsHeld()
    {
        var assignment = Pending();
        var repo = Repo(assignment);
        var handler = new UpdateAssignmentCommandHandler(
            repo.Object,
            new Mock<IIntegrationEventPublisher>().Object,
            NewCache(),
            Options.Create(new AttachmentUploadOptions()),
            new Mock<IActivityGroupLookup>().Object,
            new FakeAssignmentPolicyResolver(),
            NullLogger<UpdateAssignmentCommandHandler>.Instance);

        await AssertHeldAsync(() => handler.HandleAsync(new UpdateAssignmentCommand(
            assignment.Id, "Renamed", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null)));
    }

    [TestMethod]
    public async Task ConfirmQuestionsDraft_WhilePending_IsHeld()
    {
        var assignment = Pending();
        var handler = new ConfirmQuestionsDraftCommandHandler(
            Repo(assignment).Object, NewCache(), NullLogger<ConfirmQuestionsDraftCommandHandler>.Instance);

        await AssertHeldAsync(() => handler.HandleAsync(new ConfirmQuestionsDraftCommand(assignment.Id)));
    }

    [TestMethod]
    public async Task LinkAssignmentGroups_WhilePending_IsHeld()
    {
        var (db, tenants) = InMemoryScope("hold-link-" + Guid.NewGuid());
        await using var _db = db;
        var assignment = Pending();
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var handler = new LinkAssignmentGroupsHandler(
            db,
            new AssignmentRepository(db),
            new AssignmentActivityGroupRepository(db),
            new Mock<IActivityGroupLookup>().Object,
            tenants,
            NewCache(),
            NullLogger<LinkAssignmentGroupsHandler>.Instance);

        await AssertHeldAsync(() => handler.HandleAsync(
            new LinkAssignmentGroups(assignment.Id, [])));
    }

    [TestMethod]
    public async Task Delete_WhilePending_IsHeld()
    {
        var assignment = Pending();
        var repo = Repo(assignment);
        var handler = new DeleteAssignmentCommandHandler(
            repo.Object, NewCache(), NullLogger<DeleteAssignmentCommandHandler>.Instance);

        await AssertHeldAsync(() => handler.HandleAsync(new DeleteAssignmentCommand(assignment.Id)));
        repo.Verify(r => r.DeleteAsync(It.IsAny<Assignment>(), It.IsAny<CancellationToken>()), Times.Never,
            "the row under review must not be destroyed");
    }

    [TestMethod]
    public async Task StageQuestionsDraft_WhilePending_IsHeld()
    {
        var assignment = Pending();
        var handler = new StageQuestionsDraftCommandHandler(
            Repo(assignment).Object, NewCache(), NullLogger<StageQuestionsDraftCommandHandler>.Instance);

        await AssertHeldAsync(() => handler.HandleAsync(new StageQuestionsDraftCommand(
            assignment.Id,
            [new NewQuestionDto("Draft", QuestionTypeDto.ShortAnswer, 0, null, ResponseKinds: [QuestionResponseKindDto.Audio])])));
    }

    [TestMethod]
    public async Task DiscardQuestionsDraft_WhilePending_IsHeld()
    {
        var assignment = Pending();
        var handler = new DiscardQuestionsDraftCommandHandler(
            Repo(assignment).Object, NewCache(), NullLogger<DiscardQuestionsDraftCommandHandler>.Instance);

        await AssertHeldAsync(() => handler.HandleAsync(new DiscardQuestionsDraftCommand(assignment.Id)));
    }

    [TestMethod]
    public async Task RegenerateAttachmentExtraction_WhilePending_IsHeld()
    {
        var assignment = Pending();
        assignment.AddAttachment("file.txt", "text/plain", 3, "/staged/file.txt",
            AttachmentExtractionStatus.NotAttempted, null, null, null);
        var attachmentId = assignment.Attachments[0].Id;

        var handler = new RegenerateAttachmentExtractionCommandHandler(
            Repo(assignment).Object,
            new Mock<IFileStore>().Object,
            new Mock<IAttachmentTextExtractor>().Object,
            NewCache(),
            NullLogger<RegenerateAttachmentExtractionCommandHandler>.Instance);

        await AssertHeldAsync(() => handler.HandleAsync(
            new RegenerateAttachmentExtractionCommand(assignment.Id, attachmentId)));
    }

    [TestMethod]
    public async Task SubmitForApproval_WhilePending_IsHeld_SoTheWindowCannotBeReset()
    {
        var assignment = Pending();
        var handler = new SubmitAssignmentForApprovalCommandHandler(
            Repo(assignment).Object, NewCache(), NullLogger<SubmitAssignmentForApprovalCommandHandler>.Instance);

        await AssertHeldAsync(() => handler.HandleAsync(new SubmitAssignmentForApprovalCommand(assignment.Id)));
        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Pending);
    }

    // ══ EXEMPT rows ═════════════════════════════════════════════════════

    [TestMethod]
    public async Task Approve_WhilePending_IsExempt_BecauseItIsTheReleasePath()
    {
        var assignment = Pending();
        var handler = new ApproveAssignmentCommandHandler(
            Repo(assignment).Object, NewCache(), new FakeCurrentUser(),
            new FakeFeatureFlagService { IsEnabledValue = true },
            NullLogger<ApproveAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new ApproveAssignmentCommand(assignment.Id, ApproverId));

        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Approved);
    }

    [TestMethod]
    public async Task Reject_WhilePending_IsExempt_BecauseItIsTheReleasePath()
    {
        var assignment = Pending();
        var handler = new RejectAssignmentCommandHandler(
            Repo(assignment).Object, NewCache(), new FakeCurrentUser(),
            new FakeFeatureFlagService { IsEnabledValue = true },
            NullLogger<RejectAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new RejectAssignmentCommand(assignment.Id, ApproverId, "redo"));

        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Rejected);
    }

    [TestMethod]
    public async Task StageAttachment_WhilePending_IsExempt_ItNeverTouchesTheRow()
    {
        var assignment = Pending();
        var repo = Repo(assignment);
        var fileStore = new Mock<IFileStore>();
        fileStore.Setup(f => f.StoreAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/staged/file.txt");
        var extractor = new Mock<IAttachmentTextExtractor>();
        extractor.Setup(e => e.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachmentExtractionResult(AttachmentExtractionStatus.NotAttempted, null, null, 0));

        var handler = new StageAttachmentCommandHandler(
            fileStore.Object, extractor.Object, Options.Create(new AttachmentUploadOptions()),
            NullLogger<StageAttachmentCommandHandler>.Instance);

        await using var content = new MemoryStream("abc"u8.ToArray());
        var staged = await handler.HandleAsync(new StageAttachmentCommand(content, "file.txt", "text/plain", 3));

        staged.StoragePath.Should().Be("/staged/file.txt");
        repo.Verify(r => r.UpdateAsync(It.IsAny<Assignment>(), It.IsAny<CancellationToken>()), Times.Never,
            "staging is blob-store only — the row-level attachment write happens inside the held Update");
    }

    [TestMethod]
    public async Task RecordQuestionGeneration_WhilePending_IsExempt_ItWritesAProvenanceHeaderOnly()
    {
        var (db, _) = InMemoryScope("hold-gen-" + Guid.NewGuid());
        await using var __db = db;
        var assignment = Pending();
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var handler = new SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionGeneration.RecordQuestionGenerationCommandHandler(
            db,
            new AssignmentQuestionGenerationRepository(db),
            NullLogger<SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionGeneration.RecordQuestionGenerationCommandHandler>.Instance);

        var id = await handler.HandleAsync(
            new SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionGeneration.RecordQuestionGenerationCommand(
                assignment.Id, 5, null, null, null, null, "ollama", "gemma4:31b-cloud"));

        id.Should().NotBe(Guid.Empty);
        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Pending);
    }

    [TestMethod]
    public async Task ReviewAssignment_WhilePending_IsExempt_ItOnlyAnnotates()
    {
        var assignment = Pending();
        var handler = new ReviewAssignmentCommandHandler(
            Repo(assignment).Object, NewCache(), new FakeCurrentUser(),
            new FakeFeatureFlagService { IsEnabledValue = true },
            NullLogger<ReviewAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new ReviewAssignmentCommand(assignment.Id, TeacherId, 70m, "Looks fine"));

        assignment.Reviews.Should().ContainSingle();
        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Pending);
    }

    [TestMethod]
    public void Create_IsExempt_AFreshRowCanNeverBePending()
    {
        var assignment = Assignment.Create("New", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId);

        assignment.ApprovalStatus.Should().BeNull();
        assignment.EnsureNotAwaitingApproval();
    }

    [TestMethod]
    public async Task Duplicate_WhilePending_IsExempt_ItIsTheEscapeHatch()
    {
        var (db, tenants) = InMemoryScope("hold-dup-" + Guid.NewGuid());
        await using var _db = db;
        var assignment = Pending();
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("ASGB99");
        var handler = new DuplicateAssignmentCommandHandler(
            new AssignmentRepository(db), generator.Object, new Mock<IIntegrationEventPublisher>().Object,
            NewCache(), tenants, NullLogger<DuplicateAssignmentCommandHandler>.Instance);

        var copyId = await handler.HandleAsync(new DuplicateAssignmentCommand(assignment.Id));

        copyId.Should().NotBe(assignment.Id);
        var copy = await db.Assignments.AsNoTracking().SingleAsync(a => a.Id == copyId);
        copy.ApprovalStatus.Should().BeNull("a copy is a fresh Draft — the documented escape hatch");
        copy.SubmittedAt.Should().BeNull();
        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Pending, "the source row is untouched");
    }

    [TestMethod]
    public void Publish_WhilePending_IsExempt_BecauseTheApprovalGuardAlreadyRefusesIt()
    {
        var assignment = Pending();

        Action act = () => assignment.Publish(approvalRequired: true);

        act.Should().Throw<AssignmentApprovalRequiredException>();
        act.Should().NotThrow<AssignmentPendingApprovalException>(
            "publish is guarded by its own approval check — the hold must not shadow it");
    }

    [TestMethod]
    public void Schedule_WhilePending_IsExempt_BecauseTheApprovalGuardAlreadyRefusesIt()
    {
        var assignment = Pending();

        Action act = () => assignment.Schedule(DateTimeOffset.UtcNow.AddDays(2), approvalRequired: true);

        act.Should().Throw<AssignmentApprovalRequiredException>();
    }

    [TestMethod]
    public async Task Unpublish_WhilePending_IsExempt_BecauseItsOwnStatusGuardRefusesADraft()
    {
        var assignment = Pending();
        var handler = new UnpublishAssignmentCommandHandler(
            Repo(assignment).Object, new Mock<ISubmissionRepository>().Object,
            new Mock<IIntegrationEventPublisher>().Object, NewCache(),
            NullLogger<UnpublishAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() => handler.HandleAsync(new UnpublishAssignmentCommand(assignment.Id)))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => handler.HandleAsync(new UnpublishAssignmentCommand(assignment.Id)))
            .Should().NotThrowAsync<AssignmentPendingApprovalException>();
    }

    [TestMethod]
    public async Task Archive_WhilePending_IsExempt_BecauseItsOwnStatusGuardRefusesADraft()
    {
        var assignment = Pending();
        var handler = new ArchiveAssignmentCommandHandler(
            Repo(assignment).Object, NewCache(), NullLogger<ArchiveAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() => handler.HandleAsync(new ArchiveAssignmentCommand(assignment.Id)))
            .Should().ThrowAsync<InvalidOperationException>();
        assignment.Status.Should().Be(AssignmentStatus.Draft);
    }

    [TestMethod]
    public async Task Close_WhilePending_IsExempt_ItsGuardIsNotAProtection_SoTheSweepReadExcludesIt()
    {
        // The plan's WS2 record: Close() refuses only Archived and no-ops Closed — a Pending row DOES
        // close (Closed + Pending). It is a lifecycle op, not authoring, so it stays exempt; the
        // approval sweep's candidate read is narrowed to Status == Draft instead
        // (AssignmentLifecycleSweepQueryTests.ApprovalSweep_ExcludesAClosedPendingRow).
        var assignment = Pending();
        var handler = new CloseAssignmentCommandHandler(
            Repo(assignment).Object, new Mock<IIntegrationEventPublisher>().Object, NewCache(),
            NullLogger<CloseAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new CloseAssignmentCommand(assignment.Id));

        assignment.Status.Should().Be(AssignmentStatus.Closed);
        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Pending);
    }
}
