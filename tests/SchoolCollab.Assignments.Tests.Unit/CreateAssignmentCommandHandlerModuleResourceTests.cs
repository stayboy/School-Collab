using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;
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
/// WS-A1 coverage for <see cref="CreateAssignmentCommandHandler"/>:
/// modules + resources + total-attachment cap. Mirrors
/// <see cref="CreateAssignmentCommandHandlerQuestionsTests"/>'s
/// InMemory scaffolding. Per the round ar-4 binding coverage list:
/// validation runs BEFORE any child is added so a partial aggregate
/// can never be persisted (EC-7 analog); DisplayOrder is re-indexed
/// 0..n by list position; tenant stamping round-trips.
/// </summary>
[TestClass]
public class CreateAssignmentCommandHandlerModuleResourceTests
{
    private static readonly Guid TestTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

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
        IOptions<AttachmentUploadOptions>? uploadOptions = null)
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
            uploadOptions ?? Options.Create(new AttachmentUploadOptions()),
            NullLogger<CreateAssignmentCommandHandler>.Instance);
    }

    private static CreateAssignmentCommand SampleCommand(
        IReadOnlyList<NewContentModuleDto>? contentModules = null,
        IReadOnlyList<NewResourceDto>? resources = null,
        IReadOnlyList<NewAttachmentDto>? attachments = null) =>
        new(
            Title: "Algebra HW",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            GradeLevelId: null,
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: true,
            Questions: null,
            Attachments: attachments,
            ContentModules: contentModules,
            Resources: resources);

    [TestMethod]
    public async Task HandleAsync_HappyPath_PersistsModulesAndResources_ReindexedAndTenantStamped()
    {
        var (db, cache, tenants) = BuildScope("create-modules-resources-happy");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        // Inbound DisplayOrder 5/99 must be re-indexed 0/1 (EC-7 analog).
        var modules = new[]
        {
            new NewContentModuleDto(ModuleTypeDto.Video, "Intro", "https://example.com/v.mp4",
                StoragePath: null, DisplayOrder: 5, MinCompletionThresholdPercent: 75, IsRequired: true),
            new NewContentModuleDto(ModuleTypeDto.Guide, null, "https://example.com/g.pdf",
                StoragePath: null, DisplayOrder: 99),
        };
        var resources = new[]
        {
            new NewResourceDto(ResourceKindDto.Url, "https://example.com/article", null, "Article", true),
            new NewResourceDto(ResourceKindDto.File, null, "tenants/t/staging/g/notes.pdf", "Notes", false),
        };

        var id = await handler.HandleAsync(SampleCommand(contentModules: modules, resources: resources));

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.Modules.Should().HaveCount(2);
        stored.Modules.Select(m => m.DisplayOrder).Should().BeEquivalentTo(new[] { 0, 1 },
            opts => opts.WithStrictOrdering(),
            "the handler re-indexes DisplayOrder 0..n by list position (EC-7 analog)");
        stored.Modules[0].MinCompletionThresholdPercent.Should().Be(75);
        stored.Modules[0].IsRequired.Should().BeTrue();
        stored.Modules[1].MinCompletionThresholdPercent.Should().Be(100, "the default threshold is 100");

        stored.Resources.Should().HaveCount(2);
        stored.Resources.Select(r => r.ResourceKind).Should().BeEquivalentTo(new[] { ResourceKind.Url, ResourceKind.File });
        stored.Resources[1].StoragePath.Should().Be("tenants/t/staging/g/notes.pdf");
        stored.Resources[1].IncludedInGeneration.Should().BeFalse();

        // Tenant stamping round-trips through IgnoreQueryFilters (EC-6 analog).
        stored.Modules.Should().AllSatisfy(m => m.TenantId.Should().Be(TestTenant));
        stored.Resources.Should().AllSatisfy(r => r.TenantId.Should().Be(TestTenant));
    }

    // ── Validation rejections (FR-252: nothing persisted on failure) ──

    [TestMethod]
    public async Task HandleAsync_Module_NonHttpUrl_RejectedAndNothingPersisted()
    {
        var (db, cache, tenants) = BuildScope("create-module-bad-url");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var modules = new[] { new NewContentModuleDto(ModuleTypeDto.Video, null, "ftp://example.com/v", null, 0) };
        var act = async () => await handler.HandleAsync(SampleCommand(contentModules: modules));

        await act.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*absolute http/https URL*");
        db.Assignments.IgnoreQueryFilters().Should().BeEmpty(
            "FR-252: validation runs BEFORE any child is added — no partial aggregate persists");
    }

    [TestMethod]
    public async Task HandleAsync_Module_BlankUrl_Rejected()
    {
        var (db, cache, tenants) = BuildScope("create-module-blank-url");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var modules = new[] { new NewContentModuleDto(ModuleTypeDto.Video, null, "   ", null, 0) };
        var act = async () => await handler.HandleAsync(SampleCommand(contentModules: modules));

        await act.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*Url is required*");
    }

    [TestMethod]
    public async Task HandleAsync_Module_ThresholdOutOfRange_Rejected()
    {
        var (db, cache, tenants) = BuildScope("create-module-threshold-low");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var lowModules = new[]
        {
            new NewContentModuleDto(ModuleTypeDto.Video, null, "https://example.com/v", null, 0, MinCompletionThresholdPercent: 0),
        };
        var actLow = async () => await handler.HandleAsync(SampleCommand(contentModules: lowModules));
        await actLow.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*between 1 and 100*");

        var highModules = new[]
        {
            new NewContentModuleDto(ModuleTypeDto.Video, null, "https://example.com/v", null, 0, MinCompletionThresholdPercent: 101),
        };
        var actHigh = async () => await handler.HandleAsync(SampleCommand(contentModules: highModules));
        await actHigh.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*between 1 and 100*");
    }

    [TestMethod]
    public async Task HandleAsync_Resource_UrlKind_RequiresUrl()
    {
        var (db, cache, tenants) = BuildScope("create-resource-url-missing");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var resources = new[] { new NewResourceDto(ResourceKindDto.Url, null, null, null, true) };
        var act = async () => await handler.HandleAsync(SampleCommand(resources: resources));

        await act.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*kind Link requires Url*");
        db.Assignments.IgnoreQueryFilters().Should().BeEmpty();
    }

    [TestMethod]
    public async Task HandleAsync_Resource_FileKind_RequiresStoragePath()
    {
        var (db, cache, tenants) = BuildScope("create-resource-file-missing");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var resources = new[] { new NewResourceDto(ResourceKindDto.File, null, null, null, true) };
        var act = async () => await handler.HandleAsync(SampleCommand(resources: resources));

        await act.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*kind File requires StoragePath*");
    }

    [TestMethod]
    public async Task HandleAsync_Resource_UrlKind_MustNotCarryStoragePath()
    {
        var (db, cache, tenants) = BuildScope("create-resource-url-with-path");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var resources = new[]
        {
            new NewResourceDto(ResourceKindDto.Url, "https://example.com/x",
                StoragePath: "tenants/t/staging/g/x.pdf", null, true),
        };
        var act = async () => await handler.HandleAsync(SampleCommand(resources: resources));

        await act.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*kind Link must not carry StoragePath*");
    }

    [TestMethod]
    public async Task HandleAsync_Resource_VideoKind_RequiresExactlyOneOf_UrlOrStoragePath()
    {
        var (db, cache, tenants) = BuildScope("create-resource-video-both");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var both = new[]
        {
            new NewResourceDto(ResourceKindDto.Video, "https://example.com/v", "tenants/t/staging/g/v.mp4", null, true),
        };
        var actBoth = async () => await handler.HandleAsync(SampleCommand(resources: both));
        await actBoth.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*exactly one of Url or StoragePath*");

        var neither = new[]
        {
            new NewResourceDto(ResourceKindDto.Video, null, null, null, true),
        };
        var actNeither = async () => await handler.HandleAsync(SampleCommand(resources: neither));
        await actNeither.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*exactly one of Url or StoragePath*");
    }

    [TestMethod]
    public async Task HandleAsync_Resource_UnknownEnumValue_Rejected()
    {
        var (db, cache, tenants) = BuildScope("create-resource-unknown-enum");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var resources = new[]
        {
            new NewResourceDto((ResourceKindDto)999, "https://example.com/x", null, null, true),
        };
        var act = async () => await handler.HandleAsync(SampleCommand(resources: resources));

        await act.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*unsupported resource kind*");
    }

    [TestMethod]
    public async Task HandleAsync_TotalAttachmentCap_AboveLimit_Rejected()
    {
        var (db, cache, tenants) = BuildScope("create-attachments-total-cap");
        using var _db = db;
        var handler = NewHandler(
            db, cache, tenants,
            Options.Create(new AttachmentUploadOptions { MaxTotalSizeBytes = 100 }));

        var attachments = new[]
        {
            new NewAttachmentDto("a.pdf", "application/pdf", 60, "tenants/t/staging/g/a.pdf"),
            new NewAttachmentDto("b.pdf", "application/pdf", 60, "tenants/t/staging/g/b.pdf"),
        };
        var act = async () => await handler.HandleAsync(SampleCommand(attachments: attachments));

        await act.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*total size*exceeds the 100-byte limit*");
        db.Assignments.IgnoreQueryFilters().Should().BeEmpty();
    }

    [TestMethod]
    public async Task HandleAsync_TotalAttachmentCap_AtLimit_Accepted()
    {
        var (db, cache, tenants) = BuildScope("create-attachments-at-limit");
        using var _db = db;
        var handler = NewHandler(
            db, cache, tenants,
            Options.Create(new AttachmentUploadOptions { MaxTotalSizeBytes = 100 }));

        var attachments = new[]
        {
            new NewAttachmentDto("a.pdf", "application/pdf", 50, "tenants/t/staging/g/a.pdf"),
        };
        var id = await handler.HandleAsync(SampleCommand(attachments: attachments));

        db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id)
            .Attachments.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task HandleAsync_NullModulesAndResources_BehavesAsBefore()
    {
        var (db, cache, tenants) = BuildScope("create-null-modules-resources");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var id = await handler.HandleAsync(SampleCommand());

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.Modules.Should().BeEmpty(
            "null ContentModules/Resources keeps the pre-feature contract — no rows synthesized");
        stored.Resources.Should().BeEmpty();
    }
}
