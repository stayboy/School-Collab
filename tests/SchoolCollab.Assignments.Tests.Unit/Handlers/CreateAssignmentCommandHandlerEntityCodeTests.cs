using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit.Handlers;

/// <summary>
/// Entity-code wiring tests for <see cref="CreateAssignmentCommandHandler"/>
/// (spec §5.4). Verifies the handler invokes <see cref="IEntityCodeGenerator"/>
/// with the ASSIGNMENT_CODE rule code and assigns the generated value to
/// <c>Assignment.AssignmentNumber</c>, plus that generation failures propagate
/// and the <see cref="AssignmentCreatedIntegrationEvent"/> is enqueued.
/// <b>Cross-bounded-context:</b> the handler resolves the generator via the shared
/// <c>SchoolCollab.Core</c> contract (no direct Settings.Core reference).
/// </summary>
[TestClass]
public class CreateAssignmentCommandHandlerEntityCodeTests
{
    private static readonly Guid TestTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>Builds an InMemory AssignmentsDbContext + HybridCache + tenant provider.
    /// Mirrors the tenancy-test setup pattern in this project. Returns the disposable
    /// DbContext alongside the shared cache/tenant so the caller can dispose it.
    /// Assignments.Core grants InternalsVisibleTo so the internal repository is constructible.</summary>
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

    private static CreateAssignmentCommandHandler NewHandler(
        AssignmentsDbContext db, HybridCache cache, ITenantProvider tenants,
        Mock<IEntityCodeGenerator> generator, Mock<IIntegrationEventPublisher> publisher)
        => new(new AssignmentRepository(db),
               generator.Object,
               publisher.Object,
               cache,
               tenants,
               NullLogger<CreateAssignmentCommandHandler>.Instance);

    private static CreateAssignmentCommand SampleCommand() =>
        new(
            Title: "Algebra HW",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.TeacherGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            GradeLevelId: null,
            DueDate: null,
            MaxScore: null,
            MandatoryReview: true);

    [TestMethod]
    public async Task HandleAsync_CallsGenerator_WithAssignmentCode_AndAssignsAssignmentNumber()
    {
        var (db, cache, tenants) = BuildScope("assignment-entitycode-assign");
        using var _db = db;

        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync("ASSIGNMENT_CODE", It.IsAny<CancellationToken>()))
                 .ReturnsAsync("ASGA01");

        var publisher = new Mock<IIntegrationEventPublisher>();

        var handler = NewHandler(db, cache, tenants, generator, publisher);

        var id = await handler.HandleAsync(SampleCommand());

        var assignment = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        assignment.AssignmentNumber.Should().Be("ASGA01",
            "the handler must assign the generator output to Assignment.AssignmentNumber");

        generator.Verify(g => g.GenerateAsync("ASSIGNMENT_CODE", It.IsAny<CancellationToken>()), Times.Once);

        publisher.Verify(p => p.EnqueueAsync(
            It.Is<AssignmentCreatedIntegrationEvent>(e => e.AssignmentId == id && e.AssignmentNumber == "ASGA01"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_GenerationFailure_PropagatesAndDoesNotPersistAssignment()
    {
        var (db, cache, tenants) = BuildScope("assignment-entitycode-failure");
        using var _db = db;

        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("no active ASSIGNMENT_CODE rule"));

        var publisher = new Mock<IIntegrationEventPublisher>();

        var handler = NewHandler(db, cache, tenants, generator, publisher);

        var act = async () => await handler.HandleAsync(SampleCommand());

        await act.Should().ThrowAsync<InvalidOperationException>();
        db.Assignments.IgnoreQueryFilters().Should().BeEmpty(
            "a generation failure must not persist the assignment");
        publisher.Verify(p => p.EnqueueAsync(It.IsAny<AssignmentCreatedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}