using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-A1 coverage for <see cref="UpdateAssignmentCommandHandler"/>:
/// full-replacement semantics for modules + resources (mirrors the
/// questions + attachments pattern, decision b). Per the round ar-4
/// binding coverage list: null = preserve; non-null empty = clear;
/// validation before mutation; the InMemory owned/child replacement
/// quirk from ar-1 carries here so we assert on the captured aggregate
/// rather than the store.
/// </summary>
[TestClass]
public class UpdateAssignmentCommandHandlerModuleResourceTests
{
    private static readonly Guid TestTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private sealed class CapturingAssignmentRepository : IAssignmentRepository
    {
        public Assignment? Loaded { get; set; }
        public Assignment? Updated { get; private set; }
        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Loaded);
        public Task AddAsync(Assignment assignment, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Assignment assignment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? s, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSummary>());
        public void DetectChanges() { }
        public Task UpdateAsync(Assignment assignment, CancellationToken ct = default)
        {
            Updated = assignment;
            return Task.CompletedTask;
        }
    }

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

    private static UpdateAssignmentCommandHandler NewHandler(
        IAssignmentRepository repo, HybridCache cache,
        IOptions<AttachmentUploadOptions>? uploadOptions = null)
    {
        var publisher = new Mock<IIntegrationEventPublisher>();
        return new UpdateAssignmentCommandHandler(
            repo, publisher.Object, cache,
            uploadOptions ?? Options.Create(new AttachmentUploadOptions()),
            NullLogger<UpdateAssignmentCommandHandler>.Instance);
    }

    private static Assignment SeedDraft(AssignmentsDbContext db, ITenantProvider tenants)
    {
        var a = Assignment.Create(
            "Original", null, AssignmentType.Digital,
            GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
            TopicId, null, null, null,
            createdByTeacherId: TeacherId,
            mandatoryReview: true,
            assignmentNumber: "ASGA01")
            .WithTenant(tenants);
        a.AddModule(ModuleType.Video, "https://example.com/seed1.mp4", "Seed1");
        a.AddModule(ModuleType.Guide, "https://example.com/seed2.pdf", "Seed2");
        a.AddResource(ResourceKind.Url, url: "https://example.com/seed-article");
        db.Assignments.Add(a);
        db.SaveChanges();
        return a;
    }

    private static UpdateAssignmentCommand SampleUpdate(
        Guid id,
        IReadOnlyList<NewContentModuleDto>? contentModules = null,
        IReadOnlyList<NewResourceDto>? resources = null,
        IReadOnlyList<NewAttachmentDto>? attachments = null,
        IReadOnlyList<NewQuestionDto>? questions = null) =>
        new(
            Id: id,
            Title: "Updated",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            GradeLevelId: null,
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: true,
            Questions: questions,
            Attachments: attachments,
            ContentModules: contentModules,
            Resources: resources);

    [TestMethod]
    public async Task HandleAsync_FullReplacement_ReplacesModulesAndResources()
    {
        var (db, _, tenants) = BuildScope("update-replace-modules-resources");
        var seeded = SeedDraft(db, tenants);
        var seededId = seeded.Id;
        db.ChangeTracker.Clear();
        var loaded = db.Assignments.Single(a => a.Id == seededId);
        db.ChangeTracker.Clear();

        var (_, cache, _) = BuildScope("update-replace-modules-resources-2");
        var repo = new CapturingAssignmentRepository { Loaded = loaded };
        var handler = NewHandler(repo, cache);

        var inboundModules = new[]
        {
            new NewContentModuleDto(ModuleTypeDto.Guide, "Replaced", "https://example.com/replaced.pdf", null, 0),
        };
        var inboundResources = new[]
        {
            new NewResourceDto(ResourceKindDto.Url, "https://example.com/replaced-article", null, null, true),
        };

        await handler.HandleAsync(SampleUpdate(seededId, contentModules: inboundModules, resources: inboundResources));

        var mutated = repo.Updated!;
        mutated.Modules.Should().HaveCount(1, "the inbound non-null ContentModules fully replaces the seeded 2");
        mutated.Modules[0].Url.Should().Be("https://example.com/replaced.pdf");
        mutated.Modules[0].DisplayOrder.Should().Be(0, "EC-7 analog: DisplayOrder re-indexed 0..n by inbound list position");

        mutated.Resources.Should().HaveCount(1, "the inbound non-null Resources fully replaces the seeded 1");
        mutated.Resources[0].Url.Should().Be("https://example.com/replaced-article");

        // The old ids must be gone from the captured aggregate.
        var oldModuleIds = new[]
        {
            seeded.Modules.ElementAt(0).Id,
            seeded.Modules.ElementAt(1).Id,
        };
        mutated.Modules.Select(m => m.Id).Should().NotContain(oldModuleIds);
    }

    [TestMethod]
    public async Task HandleAsync_NullModulesAndResources_PreservesExisting()
    {
        var (db, cache, tenants) = BuildScope("update-null-modules-resources");
        var seeded = SeedDraft(db, tenants);

        var repo = new AssignmentRepository(db);
        var handler = NewHandler(repo, cache);

        await handler.HandleAsync(SampleUpdate(seeded.Id, contentModules: null, resources: null));

        db.ChangeTracker.Clear();
        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == seeded.Id);
        stored.Modules.Should().HaveCount(2, "a null ContentModules collection leaves the existing children untouched");
        stored.Resources.Should().HaveCount(1, "a null Resources collection leaves the existing children untouched");
    }

    [TestMethod]
    public async Task HandleAsync_NonNullEmpty_ContentModules_Clears_ResourcesUnaffectedWhenResourcesIsNull()
    {
        var (db, cache, tenants) = BuildScope("update-empty-modules");
        var seeded = SeedDraft(db, tenants);

        var repo = new AssignmentRepository(db);
        var handler = NewHandler(repo, cache);

        await handler.HandleAsync(SampleUpdate(seeded.Id, contentModules: [], resources: null));

        db.ChangeTracker.Clear();
        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == seeded.Id);
        stored.Modules.Should().BeEmpty("an empty non-null ContentModules collection clears the children");
        stored.Resources.Should().HaveCount(1, "a null Resources collection leaves the existing resource untouched");
    }

    [TestMethod]
    public async Task HandleAsync_InvalidModuleUrl_RejectedBeforeMutation()
    {
        var (db, cache, tenants) = BuildScope("update-invalid-module");
        var seeded = SeedDraft(db, tenants);

        var repo = new AssignmentRepository(db);
        var handler = NewHandler(repo, cache);

        var invalidModules = new[]
        {
            new NewContentModuleDto(ModuleTypeDto.Video, null, "ftp://example.com/bad", null, 0),
        };

        var act = async () => await handler.HandleAsync(
            SampleUpdate(seeded.Id, contentModules: invalidModules, resources: null));

        await act.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*absolute http/https URL*");

        // Stored aggregate must be untouched (validation before mutation).
        db.ChangeTracker.Clear();
        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == seeded.Id);
        stored.Modules.Should().HaveCount(2, "validation must reject BEFORE the aggregate is mutated");
    }
}
