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
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// R4 (spec §6A CP-5/CP-10/CP-11; round <c>assignment-context-strands-lessons</c>) — the
/// authoring context picks as a PERSISTED assignment property:
/// <list type="bullet">
///   <item><see cref="Assignment.SetContextPicks"/> semantics (AC-3) — per-kind null preserves,
///     empty clears, a non-empty list replaces and de-duplicates.</item>
///   <item>The create / update / duplicate handler wiring (AC-4/AC-5/AC-6) — the update half is
///     CP-10's null-means-preserve, empty-means-clear contract, gated per kind.</item>
///   <item>The authoring-children read mapping them back (AC-7) — always a list, never null, so
///     "no picks" has one representation.</item>
///   <item>The form model's projection filter (CP-11/AC-12) — a dangling id is dropped from the
///     save payload, and an UNKNOWN set projects null (preserve) instead of an empty clear.</item>
/// </list>
/// <para>Discriminating: none of this exists on the pre-R4 tree — the request records carry no pick
/// fields, the aggregate has no pick state, and the children DTO has nowhere to put them.</para>
/// </summary>
[TestClass]
public class AssignmentContextPicksTests
{
    private static readonly Guid TestTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
    private static readonly Guid StrandA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StrandB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LessonA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid LessonB = Guid.Parse("44444444-4444-4444-4444-444444444444");

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

    private static Assignment NewAssignment(ITenantProvider tenants) =>
        Assignment.Create(
                "Algebra HW", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
                TargetAudienceType.AllStudents, TopicId, null, null)
            .WithTenant(tenants);

    private static CreateAssignmentCommandHandler NewCreateHandler(AssignmentsDbContext db, HybridCache cache, ITenantProvider tenants)
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

    private static UpdateAssignmentCommand UpdateCommand(Guid id, IReadOnlyList<Guid>? strands, IReadOnlyList<Guid>? lessons) =>
        new(
            id, "Algebra HW", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null,
            MandatoryReview: true,
            ContextStrandIds: strands,
            ContextLessonIds: lessons);

    // ── AC-3: the entity's pick semantics ─────────────────────────────────────

    [TestMethod]
    public void FreshAssignment_HasEmptyPicks_NeverNull()
    {
        var (db, _, tenants) = BuildScope(nameof(FreshAssignment_HasEmptyPicks_NeverNull));
        using var _db = db;

        var assignment = NewAssignment(tenants);

        assignment.ContextStrandIds.Should().BeEmpty();
        assignment.ContextLessonIds.Should().BeEmpty();
    }

    [TestMethod]
    public void SetContextPicks_NonNullList_ReplacesAndDeduplicatesInFirstOccurrenceOrder()
    {
        var (db, _, tenants) = BuildScope(nameof(SetContextPicks_NonNullList_ReplacesAndDeduplicatesInFirstOccurrenceOrder));
        using var _db = db;
        var assignment = NewAssignment(tenants);

        assignment.SetContextPicks([StrandA, StrandB, StrandA], [LessonB, LessonA, LessonB]);

        assignment.ContextStrandIds.Should().Equal(new[] { StrandA, StrandB },
            "a duplicated id lands once, in first-occurrence order");
        assignment.ContextLessonIds.Should().Equal(LessonB, LessonA);
    }

    [TestMethod]
    public void SetContextPicks_NullArgument_PreservesThatKindOnly()
    {
        var (db, _, tenants) = BuildScope(nameof(SetContextPicks_NullArgument_PreservesThatKindOnly));
        using var _db = db;
        var assignment = NewAssignment(tenants);
        assignment.SetContextPicks([StrandA], [LessonA]);

        // AC-5(a)/(c) at the domain level: the two kinds are INDEPENDENT — a single is-not-null
        // branch over both would fail one half of this assertion either way.
        assignment.SetContextPicks(null, [LessonB]);
        assignment.ContextStrandIds.Should().Equal(new[] { StrandA }, "a null strand argument preserves the strand picks");
        assignment.ContextLessonIds.Should().Equal(LessonB);

        assignment.SetContextPicks([StrandB], null);
        assignment.ContextStrandIds.Should().Equal(StrandB);
        assignment.ContextLessonIds.Should().Equal(new[] { LessonB }, "a null lesson argument preserves the lesson picks");
    }

    [TestMethod]
    public void SetContextPicks_EmptyList_ClearsThatKind()
    {
        var (db, _, tenants) = BuildScope(nameof(SetContextPicks_EmptyList_ClearsThatKind));
        using var _db = db;
        var assignment = NewAssignment(tenants);
        assignment.SetContextPicks([StrandA], [LessonA]);

        assignment.SetContextPicks([], null);

        assignment.ContextStrandIds.Should().BeEmpty(
            "an empty replace is legal (unlike SetTargets' TGT-13) — it is exactly CP-10's clear");
        assignment.ContextLessonIds.Should().Equal(LessonA);
    }

    [TestMethod]
    public void SetContextPicks_StampsUpdatedAt()
    {
        var (db, _, tenants) = BuildScope(nameof(SetContextPicks_StampsUpdatedAt));
        using var _db = db;
        var assignment = NewAssignment(tenants);
        var before = assignment.UpdatedAt;

        assignment.SetContextPicks([StrandA], null);

        assignment.UpdatedAt.Should().BeOnOrAfter(before);
        assignment.UpdatedAt.Should().NotBe(default);
    }

    // ── AC-4: the create round-trip ───────────────────────────────────────────

    [TestMethod]
    public async Task CreateHandler_ThreadsThePicksOntoTheAssignment()
    {
        var (db, cache, tenants) = BuildScope(nameof(CreateHandler_ThreadsThePicksOntoTheAssignment));
        await using var _db = db;
        var handler = NewCreateHandler(db, cache, tenants);

        var id = await handler.HandleAsync(new CreateAssignmentCommand(
            Title: "Algebra HW", Description: null, AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.TeacherGraded, TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: TopicId, DueDate: null, MaxScore: null, MandatoryReview: true,
            ContextStrandIds: [StrandA, StrandB],
            ContextLessonIds: [LessonA, LessonB]));

        var persisted = await db.Assignments.SingleAsync(a => a.Id == id);
        persisted.ContextStrandIds.Should().Equal(new[] { StrandA, StrandB }, "the create payload's ids and order survive");
        persisted.ContextLessonIds.Should().Equal(LessonA, LessonB);
    }

    [TestMethod]
    public async Task CreateHandler_NoPicks_LeavesBothListsEmpty()
    {
        var (db, cache, tenants) = BuildScope(nameof(CreateHandler_NoPicks_LeavesBothListsEmpty));
        await using var _db = db;
        var handler = NewCreateHandler(db, cache, tenants);

        var id = await handler.HandleAsync(new CreateAssignmentCommand(
            Title: "Algebra HW", Description: null, AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.TeacherGraded, TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: TopicId, DueDate: null, MaxScore: null, MandatoryReview: true));

        var persisted = await db.Assignments.SingleAsync(a => a.Id == id);
        persisted.ContextStrandIds.Should().BeEmpty("null and empty both mean \"no picks\" on a create");
        persisted.ContextLessonIds.Should().BeEmpty();
    }

    // ── AC-5: the update semantics (CP-10) ───────────────────────────────────

    [TestMethod]
    public async Task UpdateHandler_NullBothKinds_PreservesThePersistedPicks()
    {
        var (db, cache, tenants) = BuildScope(nameof(UpdateHandler_NullBothKinds_PreservesThePersistedPicks));
        await using var _db = db;

        var assignment = NewAssignment(tenants);
        assignment.SetContextPicks([StrandA], [LessonA]);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        await NewUpdateHandler(db, cache).HandleAsync(UpdateCommand(assignment.Id, null, null));

        db.ChangeTracker.Clear();
        var persisted = await db.Assignments.SingleAsync(a => a.Id == assignment.Id);
        persisted.ContextStrandIds.Should().Equal(new[] { StrandA }, "CP-10: null means preserve — the scalar-only save must not wipe the picks");
        persisted.ContextLessonIds.Should().Equal(LessonA);
    }

    [TestMethod]
    public async Task UpdateHandler_EmptyList_ClearsThePicks()
    {
        var (db, cache, tenants) = BuildScope(nameof(UpdateHandler_EmptyList_ClearsThePicks));
        await using var _db = db;

        var assignment = NewAssignment(tenants);
        assignment.SetContextPicks([StrandA, StrandB], [LessonA]);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        // The ONLY expressible "remove every pick": a non-null, empty list.
        await NewUpdateHandler(db, cache).HandleAsync(UpdateCommand(assignment.Id, [], []));

        db.ChangeTracker.Clear();
        var persisted = await db.Assignments.SingleAsync(a => a.Id == assignment.Id);
        persisted.ContextStrandIds.Should().BeEmpty();
        persisted.ContextLessonIds.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpdateHandler_NonEmptyList_ReplacesThatKind()
    {
        var (db, cache, tenants) = BuildScope(nameof(UpdateHandler_NonEmptyList_ReplacesThatKind));
        await using var _db = db;

        var assignment = NewAssignment(tenants);
        assignment.SetContextPicks([StrandA], [LessonA]);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        await NewUpdateHandler(db, cache).HandleAsync(UpdateCommand(assignment.Id, [StrandA, StrandB], null));

        db.ChangeTracker.Clear();
        var persisted = await db.Assignments.SingleAsync(a => a.Id == assignment.Id);
        persisted.ContextStrandIds.Should().Equal(new[] { StrandA, StrandB }, "b was added — a full replacement, not a merge");
        persisted.ContextLessonIds.Should().Equal(new[] { LessonA }, "the untouched kind is still gated independently");
    }

    // ── AC-6: duplicate ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task DuplicateHandler_ClonesBothPickLists()
    {
        var (db, cache, tenants) = BuildScope(nameof(DuplicateHandler_ClonesBothPickLists));
        await using var _db = db;

        var source = NewAssignment(tenants);
        source.SetContextPicks([StrandA, StrandB], [LessonA]);
        db.Assignments.Add(source);
        await db.SaveChangesAsync();

        var newId = await NewDuplicateHandler(db, cache, tenants).HandleAsync(new DuplicateAssignmentCommand(source.Id));

        var clone = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == newId);
        clone.ContextStrandIds.Should().Equal(new[] { StrandA, StrandB }, "a copy carries its context picks");
        clone.ContextLessonIds.Should().Equal(LessonA);
        clone.Id.Should().NotBe(source.Id);
    }

    // ── AC-7: the authoring-children read ────────────────────────────────────

    [TestMethod]
    public async Task ChildrenRead_MapsThePersistedPicks()
    {
        var (db, _, tenants) = BuildScope(nameof(ChildrenRead_MapsThePersistedPicks));
        await using var _db = db;

        var assignment = NewAssignment(tenants);
        assignment.SetContextPicks([StrandA, StrandB], [LessonA]);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var result = await new GetAssignmentAuthoringChildrenQueryHandler(
            db, NullLogger<GetAssignmentAuthoringChildrenQueryHandler>.Instance)
            .HandleAsync(new GetAssignmentAuthoringChildrenQuery(assignment.Id));

        result.Should().NotBeNull();
        result!.ContextStrandIds.Should().Equal(StrandA, StrandB);
        result.ContextLessonIds.Should().Equal(LessonA);
    }

    [TestMethod]
    public async Task ChildrenRead_PicklessAssignment_ReturnsEmptyLists_NeverNull()
    {
        var (db, _, tenants) = BuildScope(nameof(ChildrenRead_PicklessAssignment_ReturnsEmptyLists_NeverNull));
        await using var _db = db;

        var assignment = NewAssignment(tenants);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var result = await new GetAssignmentAuthoringChildrenQueryHandler(
            db, NullLogger<GetAssignmentAuthoringChildrenQueryHandler>.Instance)
            .HandleAsync(new GetAssignmentAuthoringChildrenQuery(assignment.Id));

        // The Edit surface reads "no picks" as an authoritative empty set (it then CLEARS on
        // save); a null here would be the pre-deploy "unknown, preserve" state instead.
        result!.ContextStrandIds.Should().NotBeNull().And.BeEmpty();
        result.ContextLessonIds.Should().NotBeNull().And.BeEmpty();
    }

    // ── CP-11/AC-12: the form model's payload filter ─────────────────────────

    private static AssignmentEditFormModel ModelWithPicks(params Guid[] strandIds)
    {
        var model = new AssignmentEditFormModel();
        model.LoadContextPicks(strandIds, [LessonA]);
        return model;
    }

    private static UpdateAssignmentRequest UpdateRequest(
        AssignmentEditFormModel model,
        IReadOnlyDictionary<Guid, string>? strandMap,
        IReadOnlyDictionary<Guid, string>? lessonMap) =>
        model.ToUpdateRequest(
            AssignmentTypeDto.Digital, GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
            TopicId, mandatoryReview: true, targets: null, strandNames: strandMap, lessonNames: lessonMap);

    [TestMethod]
    public void ToUpdateRequest_ChildrenLoadedWithNoPicks_EmitsEmptyLists_SoARemovalClears()
    {
        var model = new AssignmentEditFormModel();
        model.LoadContextPicks([], []);

        var request = UpdateRequest(model, strandMap: null, lessonMap: null);

        request.ContextStrandIds.Should().NotBeNull("an empty list is the only expressible \"remove every pick\"");
        request.ContextStrandIds.Should().BeEmpty();
        request.ContextLessonIds.Should().BeEmpty();
    }

    [TestMethod]
    public void ToUpdateRequest_UnknownPicks_EmitsNull_BothKinds()
    {
        var model = new AssignmentEditFormModel();

        // The failed-children-read posture: LoadContextPicks was never called (or was called with
        // nulls), so the kinds are UNKNOWN and the wire must read "preserve".
        var request = UpdateRequest(model, strandMap: null, lessonMap: null);

        request.ContextStrandIds.Should().BeNull();
        request.ContextLessonIds.Should().BeNull();
    }

    [TestMethod]
    public void ToUpdateRequest_DanglingId_IsFilteredOutOfThePayload()
    {
        var model = ModelWithPicks(StrandA, StrandB);

        // The strand list loaded successfully and resolves only StrandA — CP-11's premise.
        var request = UpdateRequest(
            model,
            strandMap: new Dictionary<Guid, string> { [StrandA] = "Fractions" },
            lessonMap: new Dictionary<Guid, string> { [LessonA] = "Equivalent fractions" });

        request.ContextStrandIds.Should().Equal(new[] { StrandA }, "the dangling id is excluded from the save payload");
        request.ContextLessonIds.Should().Equal(LessonA);
    }

    [TestMethod]
    public void ToUpdateRequest_UnloadedList_KeepsThePicksVerbatim()
    {
        var model = ModelWithPicks(StrandA, StrandB);

        // The list did NOT load (null map = not authoritative), so nothing may be filtered: doing so
        // would silently clear a valid pick on the next save (P8-4).
        var request = UpdateRequest(model, strandMap: null, lessonMap: null);

        request.ContextStrandIds.Should().Equal(StrandA, StrandB);
        request.ContextLessonIds.Should().Equal(LessonA);
    }

    [TestMethod]
    public void ToCreateRequest_NoPicks_EmitsNull_SoTheCreateWireSaysNoPicks()
    {
        var model = new AssignmentEditFormModel();
        model.LoadContextPicks([], []);

        var request = model.ToCreateRequest(
            AssignmentTypeDto.Digital, GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
            TopicId, mandatoryReview: true);

        request.ContextStrandIds.Should().BeNull("on a create, empty and null both mean \"no picks\"");
        request.ContextLessonIds.Should().BeNull();
    }

    [TestMethod]
    public void ToCreateRequest_WithPicks_EmitsThemInOrder()
    {
        var model = new AssignmentEditFormModel();
        model.LoadContextPicks([StrandB, StrandA], [LessonA]);

        var request = model.ToCreateRequest(
            AssignmentTypeDto.Digital, GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
            TopicId, mandatoryReview: true);

        request.ContextStrandIds.Should().Equal(StrandB, StrandA);
        request.ContextLessonIds.Should().Equal(LessonA);
    }

    // ── CP-5: the snapshot mixes the same (filtered) shape as the payload ────

    private static string Snapshot(
        AssignmentEditFormModel model,
        IReadOnlyDictionary<Guid, string>? strandMap,
        IReadOnlyDictionary<Guid, string>? lessonMap) =>
        model.CaptureSaveSnapshot(
            AssignmentTypeDto.Digital, GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
            TopicId, mandatoryReview: true, strandNames: strandMap, lessonNames: lessonMap);

    [TestMethod]
    public void SaveSnapshot_ChangesWhenAPickChanges_AndWhenPicksAreCleared()
    {
        var strandMap = new Dictionary<Guid, string> { [StrandA] = "A", [StrandB] = "B" };
        var lessonMap = new Dictionary<Guid, string> { [LessonA] = "X" };

        var baseline = ModelWithPicks(StrandA);
        var added = ModelWithPicks(StrandA, StrandB);
        var cleared = new AssignmentEditFormModel();
        cleared.LoadContextPicks([], [LessonA]);

        Snapshot(added, strandMap, lessonMap).Should().NotBe(Snapshot(baseline, strandMap, lessonMap),
            "adding a pick is a payload-relevant edit");

        Snapshot(cleared, strandMap, lessonMap).Should().NotBe(Snapshot(baseline, strandMap, lessonMap),
            "deleting the last pick is a payload-relevant edit (empty=clear, not a preserve)");
    }

    [TestMethod]
    public void SaveSnapshot_IgnoresADanglingIdThePayloadAlsoDrops()
    {
        var loadedMap = new Dictionary<Guid, string> { [StrandA] = "A" };

        // StrandB is dangling in both states: the payload drops it and the snapshot mixes the
        // filtered set, so the pair stays consistent — the form is NOT dirty for a diff no save
        // could express.
        var withDangling = ModelWithPicks(StrandA, StrandB);
        var withoutDangling = ModelWithPicks(StrandA);

        Snapshot(withDangling, loadedMap, null).Should().Be(Snapshot(withoutDangling, loadedMap, null),
            "the snapshot mixes the FILTERED set, exactly as the payload does");
        UpdateRequest(withDangling, loadedMap, null).ContextStrandIds.Should().Equal(
            UpdateRequest(withoutDangling, loadedMap, null).ContextStrandIds!);
    }

    [TestMethod]
    public void SaveSnapshot_DistinguishesUnknownFromLoadedEmpty()
    {
        var loadedEmpty = new AssignmentEditFormModel();
        loadedEmpty.LoadContextPicks([], []);
        var unknown = new AssignmentEditFormModel();

        Snapshot(loadedEmpty, null, null).Should().NotBe(Snapshot(unknown, null, null),
            "\"read, empty\" (a clear) and \"unknown\" (a preserve) are two different payloads");
    }
}
