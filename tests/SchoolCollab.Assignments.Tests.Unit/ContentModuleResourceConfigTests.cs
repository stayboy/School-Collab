using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// EF config assertions for the WS-A1 standalone child entities
/// (<see cref="ContentModule"/> + <see cref="AssignmentResource"/>).
/// Mirrors <c>AssignmentSubmissionLifecycleConfigTests</c>'s
/// <c>BuildContext</c> shape and the binding coverage list in the
/// round ar-4 plan. The model⇄snapshot sync is covered separately by
/// <see cref="MigrationGuardTests"/>.
/// </summary>
[TestClass]
public class ContentModuleResourceConfigTests
{
    private static AssignmentsDbContext BuildContext()
    {
        OutboxMapping.SetFlagsFor<AssignmentsDbContext>(
            OutboxConfigurationFlags.FromConfiguration(b => b.UsePartialIndexOnOccurredAt()));

        return new AssignmentsDbContext(
            new DbContextOptionsBuilder<AssignmentsDbContext>()
                .UseNpgsql("Host=localhost;Database=guard")
                .UseSnakeCaseNamingConvention()
                .Options,
            new DesignTimeTenantProvider());
    }

    [TestMethod]
    public void ContentModule_MapsTableColumnsDefaultsAndRowVersion()
    {
        using var context = BuildContext();
        var entity = context.Model.FindEntityType(typeof(ContentModule));
        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("assignment_content_modules");

        var url = entity.FindProperty(nameof(ContentModule.Url))!;
        url.IsNullable.Should().BeFalse("Url is required for both Video and Guide modules");
        url.GetMaxLength().Should().Be(2000);

        entity.FindProperty(nameof(ContentModule.Title))!.IsNullable.Should().BeTrue();
        entity.FindProperty(nameof(ContentModule.StoragePath))!.IsNullable.Should().BeTrue();

        entity.FindProperty(nameof(ContentModule.MinCompletionThresholdPercent))!
            .GetDefaultValue().Should().Be(100);
        entity.FindProperty(nameof(ContentModule.IsRequired))!
            .GetDefaultValue().Should().Be(false);
        entity.FindProperty(nameof(ContentModule.RowVersion))!
            .GetColumnName().Should().Be("xmin");
        entity.FindProperty(nameof(ContentModule.RowVersion))!
            .GetColumnType().Should().Be("xid");

        entity.GetIndexes().Should().Contain(i =>
            !i.IsUnique &&
            i.GetDatabaseName() == "ix_assignment_content_modules_tenant_assignment" &&
            i.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { nameof(ContentModule.TenantId), nameof(ContentModule.AssignmentId) }));
    }

    [TestMethod]
    public void AssignmentResource_MapsTableColumnsAndDefaults()
    {
        using var context = BuildContext();
        var entity = context.Model.FindEntityType(typeof(AssignmentResource));
        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("assignment_resources");

        entity.FindProperty(nameof(AssignmentResource.Url))!.IsNullable.Should().BeTrue();
        entity.FindProperty(nameof(AssignmentResource.StoragePath))!.IsNullable.Should().BeTrue();
        entity.FindProperty(nameof(AssignmentResource.DisplayName))!.IsNullable.Should().BeTrue();

        entity.FindProperty(nameof(AssignmentResource.IncludedInGeneration))!
            .GetDefaultValue().Should().Be(true);

        entity.GetIndexes().Should().Contain(i =>
            !i.IsUnique &&
            i.GetDatabaseName() == "ix_assignment_resources_tenant_assignment" &&
            i.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { nameof(AssignmentResource.TenantId), nameof(AssignmentResource.AssignmentId) }));
    }

    [TestMethod]
    public void Modules_Resources_RelationshipDeclaredFromAssignmentSide()
    {
        using var context = BuildContext();

        var assignmentEntity = context.Model.FindEntityType(typeof(Assignment))!;
        var navigations = assignmentEntity.GetNavigations().ToList();

        var modulesNav = navigations.Single(n => n.Name == nameof(Assignment.Modules));
        modulesNav.IsCollection.Should().BeTrue();
        modulesNav.IsEagerLoaded.Should().BeTrue(
            "the Assignment configuration sets AutoInclude() on Modules so the wizard's review list sees them");
        var modulesFk = modulesNav.ForeignKey;
        modulesFk.Properties.Single().Name.Should().Be(nameof(ContentModule.AssignmentId));
        modulesFk.DeleteBehavior.Should().Be(DeleteBehavior.Cascade,
            "cascade covers assignment deletion (decision (b))");

        var resourcesNav = navigations.Single(n => n.Name == nameof(Assignment.Resources));
        resourcesNav.IsCollection.Should().BeTrue();
        resourcesNav.IsEagerLoaded.Should().BeTrue();
        var resourcesFk = resourcesNav.ForeignKey;
        resourcesFk.Properties.Single().Name.Should().Be(nameof(AssignmentResource.AssignmentId));
        resourcesFk.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);

        // The Questions navigation already had AutoInclude before WS-A1; assert
        // it still does (all three standalone/owned collections are auto-included).
        var questionsNav = navigations.Single(n => n.Name == nameof(Assignment.Questions));
        questionsNav.IsEagerLoaded.Should().BeTrue();
    }
}
