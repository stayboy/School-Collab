using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DuplicateAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentAuthoringChildren;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentByIdQuery;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsQuery;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.Ward;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// INS-1/INS-2 (documents/specs/assignment-authoring-compartments.md §9, R1 acceptance criterion
/// 1) — the student-facing <c>Instructions</c> field is distinct from <c>Description</c> and has
/// to survive every layer: entity → command → repository projection → read handler →
/// <see cref="AssignmentSummaryDto"/> / <see cref="WardAssignmentViewDto"/>, plus the form model
/// that the authoring page submits.
///
/// <para>Every hand-off is a positional record/command parameter with a default, so a layer that
/// forgets to thread the value does <b>not</b> fail to compile — it silently reports "" and the
/// student never sees the instructions. These tests pin each hop.</para>
/// </summary>
[TestClass]
public class AssignmentInstructionsTests
{
    private static readonly Guid TestTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private static (AssignmentsDbContext db, HybridCache cache, ITenantProvider tenants) BuildScope(string name)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(opts => opts.UseInMemoryDatabase(name));
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<AssignmentsDbContext>();
        db.Database.EnsureCreated();
        var tenants = sp.GetRequiredService<ITenantProvider>();
        ((TenantProvider)tenants).SetTenant(new TenantContext(TestTenant, "TestSchool", TenantType.School));
        return (db, sp.GetRequiredService<HybridCache>(), tenants);
    }

    private static Assignment NewAssignment(ITenantProvider tenants, string? instructions = null) =>
        Assignment.Create(
            "Algebra HW", "internal note", AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null,
            createdByTeacherId: Guid.Empty,
            mandatoryReview: true,
            instructions: instructions)
        .WithTenant(tenants);

    // ── Domain ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Create_StoresTrimmedInstructions()
    {
        var assignment = Assignment.Create(
            "Algebra HW", "internal note", AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null);

        assignment.Instructions.Should().BeNull("no instructions were supplied");

        var withInstructions = Assignment.Create(
            "Algebra HW", "internal note", AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null,
            instructions: "  Read chapter 5, then answer the questions.  ");

        withInstructions.Instructions.Should().Be("Read chapter 5, then answer the questions.",
            "instructions are stored trimmed");
        withInstructions.Description.Should().Be("internal note",
            "Instructions stay distinct from Description (INS-1)");
    }

    [TestMethod]
    public void Update_RoundTripsInstructions()
    {
        var assignment = Assignment.Create(
            "Algebra HW", "internal note", AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null,
            instructions: "original text");

        assignment.Update(
            "Algebra HW", "internal note", AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, mandatoryReview: true,
            instructions: "  revised text  ");

        assignment.Instructions.Should().Be("revised text");

        assignment.Update(
            "Algebra HW", "internal note", AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, mandatoryReview: true);

        assignment.Instructions.Should().BeNull("an update without instructions clears the field");
    }

    // ── Command handlers ────────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateHandler_ThreadsInstructionsOntoTheAssignment()
    {
        var (db, cache, tenants) = BuildScope(nameof(CreateHandler_ThreadsInstructionsOntoTheAssignment));
        using var _db = db;

        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync("ASSIGNMENT_CODE", It.IsAny<CancellationToken>()))
                 .ReturnsAsync("ASGA01");

        var handler = new CreateAssignmentCommandHandler(
            new AssignmentRepository(db),
            generator.Object,
            new Mock<IIntegrationEventPublisher>().Object,
            cache,
            tenants,
            Options.Create(new AttachmentUploadOptions()),
            new FakeCurrentUser(),
            new FakeTeacherDirectory(),
            new FakeFeatureFlagService { IsEnabledValue = true },
            new AcceptAllActivityGroupLookup(),
            new FakeAssignmentPolicyResolver(),
            NullLogger<CreateAssignmentCommandHandler>.Instance);

        var id = await handler.HandleAsync(new CreateAssignmentCommand(
            Title: "Algebra HW",
            Description: "internal note",
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.TeacherGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: TopicId,
            DueDate: null,
            MaxScore: null,
            MandatoryReview: true,
            Instructions: "Read chapter 5."));

        var persisted = await db.Assignments.SingleAsync(a => a.Id == id);
        persisted.Instructions.Should().Be("Read chapter 5.",
            "the create command must thread Instructions onto the aggregate");
        persisted.Description.Should().Be("internal note");
    }

    [TestMethod]
    public async Task UpdateHandler_UpdatesInstructions()
    {
        var (db, cache, tenants) = BuildScope(nameof(UpdateHandler_UpdatesInstructions));
        using var _db = db;

        var assignment = NewAssignment(tenants, "original text");
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var handler = new UpdateAssignmentCommandHandler(
            new AssignmentRepository(db),
            new Mock<IIntegrationEventPublisher>().Object,
            cache,
            Options.Create(new AttachmentUploadOptions()),
            new AcceptAllActivityGroupLookup(),
            new FakeAssignmentPolicyResolver(),
            NullLogger<UpdateAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new UpdateAssignmentCommand(
            assignment.Id, "Algebra HW", "internal note", AssignmentType.Digital,
            GradingFormat.TeacherGraded, TargetAudienceType.AllStudents, TopicId, null, null,
            MandatoryReview: true,
            Instructions: "Read chapter 6."));

        db.ChangeTracker.Clear();
        var persisted = await db.Assignments.SingleAsync(a => a.Id == assignment.Id);
        persisted.Instructions.Should().Be("Read chapter 6.",
            "the update command must thread Instructions onto the aggregate");
    }

    // ── Repository projections ──────────────────────────────────────────────

    [TestMethod]
    public async Task ListAsync_ProjectsInstructions()
    {
        var (db, _, tenants) = BuildScope(nameof(ListAsync_ProjectsInstructions));
        using var _db = db;

        db.Assignments.Add(NewAssignment(tenants, "Do the thing"));
        await db.SaveChangesAsync();

        var rows = await new AssignmentRepository(db).ListAsync(null);

        rows.Should().ContainSingle();
        rows[0].Instructions.Should().Be("Do the thing",
            "the list projection must carry Instructions or every read drops it silently");
    }

    [TestMethod]
    public async Task WardProjection_ProjectsInstructions()
    {
        var (db, _, tenants) = BuildScope(nameof(WardProjection_ProjectsInstructions));
        using var _db = db;

        var assignment = NewAssignment(tenants, "Do the thing");
        // The ward projection only surfaces published (or in-window scheduled) rows.
        assignment.Publish(approvalRequired: false);
        db.Assignments.Add(assignment);
        db.AssignmentRecipients.Add(AssignmentRecipient.Create(
            TestTenant, assignment.Id, ContactOwnerType.Student, StudentId, StudentId,
            Guid.NewGuid(), ContactChannel.Email, null,
            notifyOnBroadcast: true, subscriptionActive: true));
        await db.SaveChangesAsync();

        var rows = await new WardAssignmentProjectionRepository(db)
            .ListWardAssignmentsAsync(StudentId, DateTimeOffset.UtcNow);

        rows.Should().ContainSingle();
        rows[0].Instructions.Should().Be("Do the thing",
            "the ward (guardian) list projection must carry Instructions");
    }

    // ── Read handlers ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task ListAssignmentsQueryHandler_ProjectsInstructions()
    {
        var (db, cache, tenants) = BuildScope(nameof(ListAssignmentsQueryHandler_ProjectsInstructions));
        using var _db = db;

        db.Assignments.Add(NewAssignment(tenants, "Do the thing"));
        await db.SaveChangesAsync();

        var handler = new ListAssignmentsQueryHandler(
            new AssignmentRepository(db),
            cache,
            tenants,
            new FakeAssignmentPolicyResolver(),
            new FakeFeatureFlagService(),
            NullLogger<ListAssignmentsQueryHandler>.Instance);

        var rows = await handler.HandleAsync(new ListAssignmentsQuery(null));

        rows.Should().ContainSingle();
        rows[0].Instructions.Should().Be("Do the thing");
    }

    [TestMethod]
    public async Task GetAssignmentByIdQueryHandler_ProjectsInstructions()
    {
        var scope = BuildScope(nameof(GetAssignmentByIdQueryHandler_ProjectsInstructions));
        using var _db = scope.db;

        var assignment = NewAssignment(scope.tenants, "Do the thing");
        scope.db.Assignments.Add(assignment);
        await scope.db.SaveChangesAsync();

        var handler = new GetAssignmentByIdQueryHandler(
            scope.db,
            scope.cache,
            new FakeAssignmentPolicyResolver(),
            new FakeFeatureFlagService(),
            NullLogger<GetAssignmentByIdQueryHandler>.Instance);

        var dto = await handler.HandleAsync(new GetAssignmentByIdQuery(assignment.Id));

        dto.Should().NotBeNull();
        dto!.Instructions.Should().Be("Do the thing",
            "the detail read feeds the authoring page's Instructions field");
    }

    [TestMethod]
    public async Task GetWardAssignmentViewHandler_ProjectsInstructions()
    {
        var (db, _, tenants) = BuildScope(nameof(GetWardAssignmentViewHandler_ProjectsInstructions));
        using var _db = db;

        var assignment = NewAssignment(tenants, "Do the thing");
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var handler = new GetWardAssignmentViewHandler(
            new AssignmentRepository(db),
            new ModuleProgressRepository(db),
            NullLogger<GetWardAssignmentViewHandler>.Instance);

        var dto = await handler.HandleAsync(new GetWardAssignmentView(assignment.Id, StudentId));

        dto.Should().NotBeNull();
        dto!.Instructions.Should().Be("Do the thing",
            "INS-2: the ward player renders the instructions read-only");
    }

    // ── Form model ──────────────────────────────────────────────────────────

    [TestMethod]
    public void FormModel_LoadsAndProjectsInstructions()
    {
        var dto = new AssignmentSummaryDto(
            Id: Guid.NewGuid(),
            Title: "Algebra HW",
            Description: "internal note",
            AssignmentType: AssignmentTypeDto.Digital,
            GradingFormat: GradingFormatDto.TeacherGraded,
            TargetAudienceType: TargetAudienceTypeDto.AllStudents,
            TopicId: TopicId,
            TopicName: "Math",
            Status: AssignmentStatusDto.Draft,
            DueDate: null,
            MaxScore: null,
            MandatoryReview: true,
            CreatedByTeacherId: Guid.Empty,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            Instructions: "Do the thing");

        var model = AssignmentEditFormModel.From(dto);
        model.Instructions.Should().Be("Do the thing", "LoadFrom must carry Instructions");

        var create = model.ToCreateRequest(
            AssignmentTypeDto.Digital, GradingFormatDto.TeacherGraded,
            TargetAudienceTypeDto.AllStudents, TopicId, true);
        create.Instructions.Should().Be("Do the thing");

        model.Instructions = "Edited text";
        var update = model.ToUpdateRequest(
            AssignmentTypeDto.Digital, GradingFormatDto.TeacherGraded,
            TargetAudienceTypeDto.AllStudents, TopicId, true);
        update.Instructions.Should().Be("Edited text", "ToUpdateRequest must carry Instructions");
    }

    // ── Round media-pill-listing (Q7, Slice F): DurationSeconds across every projection ──────────

    private static CreateAssignmentCommandHandler NewCreateHandler(
        AssignmentsDbContext db, HybridCache cache, ITenantProvider tenants)
    {
        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync("ASGA01");
        return new CreateAssignmentCommandHandler(
            new AssignmentRepository(db),
            generator.Object,
            new Mock<IIntegrationEventPublisher>().Object,
            cache,
            tenants,
            Options.Create(new AttachmentUploadOptions()),
            new FakeCurrentUser(),
            new FakeTeacherDirectory(),
            new FakeFeatureFlagService { IsEnabledValue = true },
            new AcceptAllActivityGroupLookup(),
            new FakeAssignmentPolicyResolver(),
            NullLogger<CreateAssignmentCommandHandler>.Instance);
    }

    private static UpdateAssignmentCommandHandler NewUpdateHandler(AssignmentsDbContext db, HybridCache cache) =>
        new(
            new AssignmentRepository(db),
            new Mock<IIntegrationEventPublisher>().Object,
            cache,
            Options.Create(new AttachmentUploadOptions()),
            new AcceptAllActivityGroupLookup(),
            new FakeAssignmentPolicyResolver(),
            NullLogger<UpdateAssignmentCommandHandler>.Instance);

    private static DuplicateAssignmentCommandHandler NewDuplicateHandler(
        AssignmentsDbContext db, HybridCache cache, ITenantProvider tenants)
    {
        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync("DUP999");
        return new DuplicateAssignmentCommandHandler(
            new AssignmentRepository(db),
            generator.Object,
            new Mock<IIntegrationEventPublisher>().Object,
            cache,
            tenants,
            NullLogger<DuplicateAssignmentCommandHandler>.Instance);
    }

    private static NewInstructionDto MediaInstruction(InstructionKindDto kind, string fileName, string contentType, int durationSeconds) =>
        new(Kind: kind, Text: null, Url: null, FileName: fileName, ContentType: contentType,
            FileSize: 2048, StoragePath: $"tenants/t/staging/g/{fileName}", Title: null,
            DurationSeconds: durationSeconds);

    private static NewQuestionDto InstructionQuestion(NewInstructionDto instruction) =>
        new(QuestionText: "Explain the working.", QuestionType: QuestionTypeDto.ShortAnswer,
            DisplayOrder: 0, Options: null, ModelAnswer: null, GenerationId: null,
            ResponseKinds: null, Instructions: [instruction]);

    /// <summary>Q7/AC-9/Slice F: the duration survives ALL FIVE ctor-feeding projections — the create
    /// pair (:107/:188), the update triple (the :79 null→preserve branch, :90 command rows, :192
    /// question rows) and the duplicate pair (:94/:124). The duplicate handler is the trap: with the
    /// trailing-defaulted ctor param an unthreaded site compiles unchanged and silently nulls every
    /// duplicated duration.</summary>
    [TestMethod]
    public async Task UpdateInstructions_LoadedRowDuration_SurvivesTheNullPreservePath()
    {
        var (db, cache, tenants) = BuildScope(nameof(UpdateInstructions_LoadedRowDuration_SurvivesTheNullPreservePath));
        using var _db = db;

        // ── Create: both instruction-tuple projections thread the field. ──
        var createHandler = NewCreateHandler(db, cache, tenants);
        var id = await createHandler.HandleAsync(new CreateAssignmentCommand(
            Title: "Algebra HW", Description: null, AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded, TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: TopicId, DueDate: null, MaxScore: null,
            Questions: [InstructionQuestion(MediaInstruction(InstructionKindDto.Video, "q.webm", "video/webm", 90))],
            InstructionItems: [MediaInstruction(InstructionKindDto.Audio, "a.mp3", "audio/mpeg", 154)]));

        db.ChangeTracker.Clear();
        var created = await db.Assignments.IgnoreQueryFilters().SingleAsync(a => a.Id == id);
        created.InstructionsFor(null).Single().DurationSeconds.Should().Be(154,
            "CreateAssignmentCommandHandler :107 threads the assignment-own row's duration");
        created.InstructionsFor(created.Questions.Single().Id).Single().DurationSeconds.Should().Be(90,
            "…and :188 threads each question row's");

        // ── Update with a NULL instruction payload: preserve must copy the existing duration. ──
        var updateHandler = NewUpdateHandler(db, cache);
        await updateHandler.HandleAsync(new UpdateAssignmentCommand(
            id, "Algebra HW", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, MandatoryReview: true));

        db.ChangeTracker.Clear();
        var preserved = await db.Assignments.IgnoreQueryFilters().SingleAsync(a => a.Id == id);
        preserved.InstructionsFor(null).Single().DurationSeconds.Should().Be(154,
            "UpdateAssignmentCommandHandler :79 (null→preserve) must copy existing.DurationSeconds or an "
            + "unrelated edit silently strips every saved duration");

        // ── Update with command rows (:90) and re-minted question rows (:192). ──
        await updateHandler.HandleAsync(new UpdateAssignmentCommand(
            id, "Algebra HW", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, MandatoryReview: true,
            Questions: [InstructionQuestion(MediaInstruction(InstructionKindDto.Video, "q2.webm", "video/webm", 91))],
            InstructionItems: [MediaInstruction(InstructionKindDto.Audio, "a2.mp3", "audio/mpeg", 155)]));

        db.ChangeTracker.Clear();
        var updated = await db.Assignments.IgnoreQueryFilters().SingleAsync(a => a.Id == id);
        updated.InstructionsFor(null).Single().DurationSeconds.Should().Be(155, ":90 threads command rows");
        updated.InstructionsFor(updated.Questions.Single().Id).Single().DurationSeconds.Should().Be(91,
            ":192 threads the re-minted question rows");

        // ── Duplicate: both re-projection sites copy the persisted durations. ──
        var duplicateHandler = NewDuplicateHandler(db, cache, tenants);
        var duplicateId = await duplicateHandler.HandleAsync(new DuplicateAssignmentCommand(id));

        db.ChangeTracker.Clear();
        var clone = await db.Assignments.IgnoreQueryFilters().SingleAsync(a => a.Id == duplicateId);
        clone.InstructionsFor(null).Single().DurationSeconds.Should().Be(155,
            "DuplicateAssignmentCommandHandler :94 copies the assignment-own row's duration");
        clone.InstructionsFor(clone.Questions.Single().Id).Single().DurationSeconds.Should().Be(91,
            "…and :124 copies each question row's");
    }

    /// <summary>Q7/Slice F: both read-mappers emit the persisted duration — and a legacy row (whose
    /// column is null) reads null, never an invented value.</summary>
    [TestMethod]
    public async Task GetAuthoringChildren_AndWardView_MaterialsCarryDuration()
    {
        var (db, cache, tenants) = BuildScope(nameof(GetAuthoringChildren_AndWardView_MaterialsCarryDuration));
        using var _db = db;

        var assignment = NewAssignment(tenants);
        assignment.SetInstructionItems(
        [
            (null, InstructionKind.Audio, null, null, "a.mp3", "audio/mpeg", 2048, "staged/a.mp3", null, 154),
            (null, InstructionKind.Image, null, null, "d.png", "image/png", 2048, "staged/d.png", "Diagram", null),
        ]);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var authoring = new GetAssignmentAuthoringChildrenQueryHandler(
            db, NullLogger<GetAssignmentAuthoringChildrenQueryHandler>.Instance);
        var children = await authoring.HandleAsync(new GetAssignmentAuthoringChildrenQuery(assignment.Id));

        children.Should().NotBeNull();
        children!.InstructionItems.Should().NotBeNull();
        children.InstructionItems!.Single(i => i.Kind == InstructionKindDto.Audio).DurationSeconds.Should().Be(154,
            "the authoring child read feeds the editor row, whose readout renders m:ss");
        children.InstructionItems!.Single(i => i.Kind == InstructionKindDto.Image).DurationSeconds.Should().BeNull(
            "a row written before the column carries none — nothing invents one (the readout shows —:—)");

        var ward = new GetWardAssignmentViewHandler(
            new AssignmentRepository(db),
            new ModuleProgressRepository(db),
            NullLogger<GetWardAssignmentViewHandler>.Instance);
        var wardView = await ward.HandleAsync(new GetWardAssignmentView(assignment.Id, StudentId));

        wardView.Should().NotBeNull();
        wardView!.Materials!.Single(m => m.Kind == InstructionKindDto.Audio).DurationSeconds.Should().Be(154);
        wardView!.Materials!.Single(m => m.Kind == InstructionKindDto.Image).DurationSeconds.Should().BeNull();
    }
}
