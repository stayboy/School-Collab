using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit.Tenancy;

/// <summary>
/// WS-A1 tenancy coverage for the standalone child entities
/// (<see cref="ContentModule"/> + <see cref="AssignmentResource"/>).
/// Mirrors the InMemory + AddTenancy + SetTenant scaffolding from
/// <see cref="AssignmentOwnedTypeTenancyTests"/>. AC-6 analog: a query as
/// tenant A returns only tenant A's rows; tenant B's rows are never
/// surfaced; <c>IgnoreQueryFilters(["Tenant"])</c> shows the full set.
/// </summary>
[TestClass]
public class ContentModuleResourceTenancyTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static async Task<ServiceProvider> BuildProviderAsync(string dbName)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(opts =>
            opts.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();

        using (var bootScope = provider.CreateScope())
        {
            var bootCtx = bootScope.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            await bootCtx.Database.EnsureCreatedAsync();
        }
        return provider;
    }

    private static void AsTenant(ServiceProvider provider, Guid tenantId)
    {
        var tenants = provider.GetRequiredService<ITenantProvider>();
        ((TenantProvider)tenants).SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));
    }

    private static Assignment NewDraft() =>
        Assignment.Create("HW", null, AssignmentType.Digital,
            GradingFormat.TeacherGraded, TargetAudienceType.AllStudents,
            TopicId, null, null, null, TeacherId);

    [TestMethod]
    public async Task Modules_And_Resources_AreTenantIsolated_NoCrossTenantLeak()
    {
        using var provider = await BuildProviderAsync("ws-a1-tenancy-modules-resources");

        // Tenant A: one assignment with one module + one resource.
        AsTenant(provider, TenantA);
        Guid assignmentAId;
        using (var scopeA = provider.CreateScope())
        {
            var dbA = scopeA.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            var a = NewDraft().WithTenant(TenantA);
            a.AddModule(ModuleType.Video, "https://example.com/A-intro.mp4");
            a.AddResource(ResourceKind.Url, url: "https://example.com/A-article");
            dbA.Assignments.Add(a);
            await dbA.SaveChangesAsync();
            assignmentAId = a.Id;
        }

        // Tenant B: one assignment with one module + one resource.
        AsTenant(provider, TenantB);
        Guid assignmentBId;
        using (var scopeB = provider.CreateScope())
        {
            var dbB = scopeB.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            var b = NewDraft().WithTenant(TenantB);
            b.AddModule(ModuleType.Guide, "https://example.com/B-guide.pdf");
            b.AddResource(ResourceKind.File, storagePath: "tenants/B/staging/g/B-notes.pdf");
            dbB.Assignments.Add(b);
            await dbB.SaveChangesAsync();
            assignmentBId = b.Id;
        }

        // Tenant A's view: only A's rows.
        AsTenant(provider, TenantA);
        using (var scopeA2 = provider.CreateScope())
        {
            var dbA = scopeA2.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            dbA.ContentModules.Should().HaveCount(1);
            dbA.AssignmentResources.Should().HaveCount(1);
            dbA.Assignments.Include(x => x.Modules).Single().Id.Should().Be(assignmentAId);
            dbA.Assignments.Include(x => x.Modules).Single().Modules
                .Should().ContainSingle().Which.Url.Should().Contain("/A-intro");
            dbA.Assignments.Include(x => x.Resources).Single().Resources
                .Should().ContainSingle().Which.Url.Should().Contain("/A-article");
        }

        // Tenant B's view: only B's rows.
        AsTenant(provider, TenantB);
        using (var scopeB2 = provider.CreateScope())
        {
            var dbB = scopeB2.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            dbB.ContentModules.Should().HaveCount(1);
            dbB.AssignmentResources.Should().HaveCount(1);
            dbB.Assignments.Include(x => x.Modules).Single().Id.Should().Be(assignmentBId);
            dbB.Assignments.Include(x => x.Resources).Single().Resources
                .Should().ContainSingle().Which.StoragePath.Should().StartWith("tenants/B/");
        }

        // Cross-tenant projection: IgnoreQueryFilters(["Tenant"]) shows both rows
        // with the correct TenantId stamp (the sweep uses this opt-out).
        using var allScope = provider.CreateScope();
        var allDb = allScope.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
        allDb.ContentModules.IgnoreQueryFilters(["Tenant"]).Should().HaveCount(2);
        allDb.ContentModules.IgnoreQueryFilters(["Tenant"])
            .Select(m => m.TenantId).Should().BeEquivalentTo(new[] { TenantA, TenantB });
        allDb.AssignmentResources.IgnoreQueryFilters(["Tenant"]).Should().HaveCount(2);
        allDb.AssignmentResources.IgnoreQueryFilters(["Tenant"])
            .Select(r => r.TenantId).Should().BeEquivalentTo(new[] { TenantA, TenantB });
    }
}
