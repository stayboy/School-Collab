using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// The assignment's three <b>policy-derived terms</b> — <c>RequiresSignature</c>,
/// <c>MandatoryReview</c> and <c>ArchiveGraceDays</c> — end to end.
///
/// <para>Round WS-C1 pinned the domain defaults + the create/update handler threading of the
/// author-supplied signature flag. Round <c>assignment-rules-policy-rework</c> (D4/D6/D10, AC3/AC4/AC12)
/// replaces that author input with a <b>snapshot of the resolved effective policy</b>: the handlers
/// source the three values from <see cref="IAssignmentPolicyResolver"/> (never the request), the
/// domain asserts the D4 implication (<c>RequiresSignature ⇒ MandatoryReview</c>) as a backstop, and an
/// update re-snapshots at explicit save time only — a later policy change never retro-applies to an
/// existing row.</para>
/// </summary>
[TestClass]
public class SignatureDefaultsTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly EffectiveAssignmentPolicyResolver Resolver = new();

    private static EffectiveAssignmentPolicy PolicyWith(
        SignatureRequirementMode signature = SignatureRequirementMode.Disabled,
        bool? mandatoryReview = null,
        int? archiveGraceDays = null) =>
        Resolver.Resolve(
            tenantDefault: new AssignmentPolicyFields
            {
                SignatureRequirement = signature,
                MandatoryReview = mandatoryReview,
                ArchiveGraceDays = archiveGraceDays,
            },
            gradeOverride: null);

    [TestMethod]
    public void Assignment_Create_DefaultsRequiresSignatureFalse()
    {
        var a = Assignment.Create(
            "Test", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, Guid.NewGuid(), null, null);
        a.RequiresSignature.Should().BeFalse();
    }

    [TestMethod]
    public void Assignment_Create_WithRequiresSignature_StoresFlag()
    {
        var a = Assignment.Create(
            "Test", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, Guid.NewGuid(), null, null,
            requiresSignature: true);
        a.RequiresSignature.Should().BeTrue();
    }

    [TestMethod]
    public void Assignment_Update_RequiresSignature_RoundTrips()
    {
        var a = Assignment.Create(
            "Test", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, Guid.NewGuid(), null, null);
        a.RequiresSignature.Should().BeFalse();

        a.Update("Updated", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, Guid.NewGuid(), null, null,
            mandatoryReview: true, requiresSignature: true);
        a.RequiresSignature.Should().BeTrue();
    }

    // ── AC3 (D4): the domain backstop ───────────────────────────────────────

    /// <summary>The <c>Create</c> half of the D4 guard, mirroring the existing
    /// <c>passScore ≤ maxScore</c> backstop: a caller that bypasses the effective-policy resolver
    /// cannot persist a signed assignment whose guardian review is off.</summary>
    [TestMethod]
    public void Assignment_Create_SignatureWithoutReview_Throws()
    {
        var act = () => Assignment.Create(
            "Test", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, Guid.NewGuid(), null, null,
            mandatoryReview: false,
            requiresSignature: true);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("mandatoryReview")
            .WithMessage("*guardian review*");
    }

    [TestMethod]
    public void Assignment_Update_SignatureWithoutReview_Throws()
    {
        var a = Assignment.Create(
            "Test", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, Guid.NewGuid(), null, null);

        var act = () => a.Update("Updated", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, Guid.NewGuid(), null, null,
            mandatoryReview: false, requiresSignature: true);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("mandatoryReview")
            .WithMessage("*guardian review*");
    }

    // ── AC4 (D6): the snapshot at create ────────────────────────────────────

    /// <summary>AC4: the resolved policy beats the author payload for guardian review, and an unset
    /// archive window falls back to the built-in 30-day retention floor (OD2).</summary>
    [TestMethod]
    public async Task CreateAssignmentHandler_SnapshotsTheThreeTermsFromTheResolvedPolicy()
    {
        var scope = BuildScope("policy-terms-create");
        var resolver = new FakeAssignmentPolicyResolver { Policy = PolicyWith(mandatoryReview: true) };
        var handler = NewCreateHandler(scope.db, scope.cache, scope.tenants, resolver);

        var id = await handler.HandleAsync(new CreateAssignmentCommand(
            Title: "Policy HW",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: false));

        var saved = await scope.db.Assignments.SingleAsync(a => a.Id == id);
        saved.MandatoryReview.Should().BeTrue(
            "a resolved policy value always beats the author payload (OD1)");
        saved.RequiresSignature.Should().BeFalse("the policy sets no signature requirement");
        saved.ArchiveGraceDays.Should().Be(30,
            "an unset window keeps the built-in retention floor (OD2)");
    }

    /// <summary>OD5 + D4: a resolved <c>Optional</c> signature requirement stores
    /// <c>RequiresSignature = true</c> and therefore pins guardian review on.</summary>
    [TestMethod]
    public async Task CreateAssignmentHandler_OptionalSignaturePolicy_StoresSignatureAndReview()
    {
        var scope = BuildScope("policy-terms-create-optional");
        var resolver = new FakeAssignmentPolicyResolver { Policy = PolicyWith(SignatureRequirementMode.Optional) };
        var handler = NewCreateHandler(scope.db, scope.cache, scope.tenants, resolver);

        var id = await handler.HandleAsync(new CreateAssignmentCommand(
            Title: "Policy HW",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: false));

        var saved = await scope.db.Assignments.SingleAsync(a => a.Id == id);
        saved.RequiresSignature.Should().BeTrue("OD5: Optional stores a signature requirement");
        saved.MandatoryReview.Should().BeTrue("D4: the signature requirement implies guardian review");
    }

    /// <summary>OD1/OD2, fail-open half: with nothing resolved the author's review value stands and
    /// the built-in defaults apply.</summary>
    [TestMethod]
    public async Task CreateAssignmentHandler_UnsetPolicy_KeepsTheAuthorReviewAndTheBuiltInDefaults()
    {
        var scope = BuildScope("policy-terms-create-unset");
        var handler = NewCreateHandler(scope.db, scope.cache, scope.tenants, new FakeAssignmentPolicyResolver());

        var id = await handler.HandleAsync(new CreateAssignmentCommand(
            Title: "Policy HW",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: false));

        var saved = await scope.db.Assignments.SingleAsync(a => a.Id == id);
        saved.MandatoryReview.Should().BeFalse("an unset policy leaves the author's choice standing");
        saved.RequiresSignature.Should().BeFalse();
        saved.ArchiveGraceDays.Should().Be(30);
    }

    // ── AC12 (D6): the re-snapshot at update ─────────────────────────────────

    /// <summary>AC12: an update re-snapshots all three terms from the CURRENTLY resolved policy, at
    /// explicit save time only — a policy change after the save never touches the stored row.</summary>
    [TestMethod]
    public async Task UpdateAssignmentHandler_ReSnapshotsTheThreeTermsAtSave()
    {
        var scope = BuildScope("policy-terms-update");
        var tenants = scope.tenants;
        var repo = new AssignmentRepository(scope.db);
        var createHandler = NewCreateHandler(scope.db, scope.cache, tenants, new FakeAssignmentPolicyResolver());

        var id = await createHandler.HandleAsync(new CreateAssignmentCommand(
            Title: "Policy HW",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: false));

        // The policy changes between the create and the next explicit save.
        var resolver = new FakeAssignmentPolicyResolver
        {
            Policy = PolicyWith(SignatureRequirementMode.Mandatory, archiveGraceDays: 45),
        };
        var updateHandler = NewUpdateHandler(repo, scope.cache, resolver);

        await updateHandler.HandleAsync(new UpdateAssignmentCommand(
            Id: id,
            Title: "Policy HW Updated",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: false));

        var saved = await scope.db.Assignments.SingleAsync(a => a.Id == id);
        saved.RequiresSignature.Should().BeTrue("OD5/D6: the update re-snapshots the signature requirement");
        saved.MandatoryReview.Should().BeTrue("D4 wins over the author payload sent with the update (OD1)");
        saved.ArchiveGraceDays.Should().Be(45, "the update re-snapshots the archive window too (D6)");
    }

    /// <summary>AC12: the update resolves the policy for the assignment's DERIVED policy-scope grade —
    /// the one-distinct-grade rule shared with the publish path.</summary>
    [TestMethod]
    public async Task UpdateAssignmentHandler_ResolvesThePolicyForTheDerivedPolicyScopeGrade()
    {
        var scope = BuildScope("policy-terms-update-grade");
        var tenants = scope.tenants;
        var repo = new AssignmentRepository(scope.db);
        var createHandler = NewCreateHandler(scope.db, scope.cache, tenants, new FakeAssignmentPolicyResolver());

        var id = await createHandler.HandleAsync(new CreateAssignmentCommand(
            Title: "Policy HW",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: false));

        var gradeId = Guid.NewGuid();
        var assignment = await scope.db.Assignments.SingleAsync(a => a.Id == id);
        assignment.SetTargets([(TargetKind.GradeLevel, gradeId)], assignment.TenantId);
        await scope.db.SaveChangesAsync();

        var resolver = new FakeAssignmentPolicyResolver();
        var updateHandler = NewUpdateHandler(repo, scope.cache, resolver);

        await updateHandler.HandleAsync(new UpdateAssignmentCommand(
            Id: id,
            Title: "Policy HW Updated",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: true));

        resolver.RequestedGradeLevelIds.Should().Equal([gradeId],
            "exactly one grade target derives that grade's effective policy (AssignmentPolicyScope)");
    }

    // ── Scaffolding (mirrors CreateAssignmentCommandHandlerQuestionsTests.BuildScope) ──

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
        ((TenantProvider)tenants).SetTenant(new TenantContext(TenantId, "TestSchool", TenantType.School));
        return (db, sp.GetRequiredService<HybridCache>(), tenants);
    }

    private static CreateAssignmentCommandHandler NewCreateHandler(
        AssignmentsDbContext db, HybridCache cache, ITenantProvider tenants,
        FakeAssignmentPolicyResolver policyResolver)
    {
        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync("ASGA01");
        var publisher = new Mock<IIntegrationEventPublisher>();
        return new CreateAssignmentCommandHandler(
            new AssignmentRepository(db),
            generator.Object,
            publisher.Object,
            cache,
            tenants,
            Options.Create(new AttachmentUploadOptions()),
            new FakeCurrentUser(),
            new FakeTeacherDirectory(),
            new FakeFeatureFlagService { IsEnabledValue = true },
            new AcceptAllActivityGroupLookup(),
            policyResolver,
            NullLogger<CreateAssignmentCommandHandler>.Instance);
    }

    private static UpdateAssignmentCommandHandler NewUpdateHandler(
        IAssignmentRepository repo, HybridCache cache, FakeAssignmentPolicyResolver policyResolver) =>
        new(repo,
            new Mock<IIntegrationEventPublisher>().Object,
            cache,
            Options.Create(new AttachmentUploadOptions()),
            new AcceptAllActivityGroupLookup(),
            policyResolver,
            NullLogger<UpdateAssignmentCommandHandler>.Instance);
}
