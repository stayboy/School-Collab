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
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-C1 (spec §7 Q1) coverage for the guardian-signature flag: the domain
/// defaults + create/update handler threading end-to-end.
/// </summary>
[TestClass]
public class SignatureDefaultsTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [TestMethod]
    public void Assignment_Create_DefaultsRequiresSignatureFalse()
    {
        var a = Assignment.Create(
            "Test", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, Guid.NewGuid(), null, null, null);
        a.RequiresSignature.Should().BeFalse();
    }

    [TestMethod]
    public void Assignment_Create_WithRequiresSignature_StoresFlag()
    {
        var a = Assignment.Create(
            "Test", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, Guid.NewGuid(), null, null, null,
            requiresSignature: true);
        a.RequiresSignature.Should().BeTrue();
    }

    [TestMethod]
    public void Assignment_Update_RequiresSignature_RoundTrips()
    {
        var a = Assignment.Create(
            "Test", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, Guid.NewGuid(), null, null, null);
        a.RequiresSignature.Should().BeFalse();

        a.Update("Updated", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, Guid.NewGuid(), null, null, null,
            mandatoryReview: true, requiresSignature: true);
        a.RequiresSignature.Should().BeTrue();
    }

    [TestMethod]
    public async Task CreateAssignmentHandler_ThreadsRequiresSignature()
    {
        var scope = BuildScope("sig-defaults-create");
        var handler = NewCreateHandler(scope.db, scope.cache, scope.tenants);

        var id = await handler.HandleAsync(new CreateAssignmentCommand(
            Title: "Sig HW",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            GradeLevelId: null,
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: true,
            RequiresSignature: true));

        var saved = await scope.db.Assignments.SingleAsync(a => a.Id == id);
        saved.RequiresSignature.Should().BeTrue();
    }

    [TestMethod]
    public async Task UpdateAssignmentHandler_ThreadsRequiresSignature()
    {
        var scope = BuildScope("sig-defaults-update");
        var tenants = scope.tenants;
        var repo = new AssignmentRepository(scope.db);
        var createHandler = NewCreateHandler(scope.db, scope.cache, tenants);

        var id = await createHandler.HandleAsync(new CreateAssignmentCommand(
            Title: "Sig HW",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            GradeLevelId: null,
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: true));

        var updateHandler = new UpdateAssignmentCommandHandler(
            repo, new Mock<IIntegrationEventPublisher>().Object, scope.cache,
            Options.Create(new AttachmentUploadOptions()),
            NullLogger<UpdateAssignmentCommandHandler>.Instance);

        await updateHandler.HandleAsync(new UpdateAssignmentCommand(
            Id: id,
            Title: "Sig HW Updated",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            GradeLevelId: null,
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: true,
            RequiresSignature: true));

        var saved = await scope.db.Assignments.SingleAsync(a => a.Id == id);
        saved.RequiresSignature.Should().BeTrue();
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
        AssignmentsDbContext db, HybridCache cache, ITenantProvider tenants)
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
            NullLogger<CreateAssignmentCommandHandler>.Instance);
    }
}