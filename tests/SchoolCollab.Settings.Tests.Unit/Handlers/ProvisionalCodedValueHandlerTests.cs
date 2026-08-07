using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.ApproveProvisionalCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateProvisionalCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RejectProvisionalCodedValue;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.ListProvisionalCodedValues;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Data.Repositories;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

[TestClass]
public class ProvisionalCodedValueHandlerTests
{
    private sealed class MutableTenantProvider : ITenantProvider
    {
        public TenantContext Current { get; set; } = new(Guid.Empty, "System", TenantType.Organization);
        public TenantContext GetTenantContext() => Current;
    }

    private sealed class Scope : IDisposable
    {
        public SettingsDbContext Db { get; }
        public MutableTenantProvider Tenants { get; } = new();
        public HybridCache Cache { get; }
        public Mock<IIntegrationEventPublisher> Publisher { get; } = new();
        public CreateProvisionalCodedValueHandler Create { get; }
        public ApproveProvisionalCodedValueHandler Approve { get; }
        public RejectProvisionalCodedValueHandler Reject { get; }
        public ListProvisionalCodedValuesHandler List { get; }

        public Scope(string dbName)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITenantProvider>(Tenants);
            services.AddDbContext<SettingsDbContext>(o => o.UseInMemoryDatabase(dbName));
            services.AddScoped<ICodedValueRepository, CodedValueRepository>();
            services.AddDistributedMemoryCache();
            services.AddHybridCache();
            var provider = services.BuildServiceProvider();

            Db = provider.GetRequiredService<SettingsDbContext>();
            Cache = provider.GetRequiredService<HybridCache>();
            var repository = provider.GetRequiredService<ICodedValueRepository>();
            var accessor = new TenantContextAccessor(new TenantProvider());

            Create = new CreateProvisionalCodedValueHandler(
                repository, Publisher.Object, Cache, Tenants,
                NullLogger<CreateProvisionalCodedValueHandler>.Instance);
            Approve = new ApproveProvisionalCodedValueHandler(
                Db, accessor, Cache, NullLogger<ApproveProvisionalCodedValueHandler>.Instance);
            Reject = new RejectProvisionalCodedValueHandler(
                Db, accessor, Cache, NullLogger<RejectProvisionalCodedValueHandler>.Instance);
            List = new ListProvisionalCodedValuesHandler(Db);
        }

        public void Dispose() => Db.Dispose();
    }

    private static Guid TenantA() => Guid.NewGuid();
    private static Guid TenantB() => Guid.NewGuid();

    private static async Task<Guid> SeedSharedAsync(SettingsDbContext db, string code, string name)
    {
        var cv = CodedValue.Create(code, name, null, null, 0);
        db.CodedValues.Add(cv);
        await db.SaveChangesAsync();
        return cv.Id;
    }

    [TestMethod]
    public async Task Create_ForRealTenant_CreatesProvisionalTenantOwnedValue()
    {
        using var s = new Scope(nameof(Create_ForRealTenant_CreatesProvisionalTenantOwnedValue));
        var tenant = TenantA();
        s.Tenants.Current = new TenantContext(tenant, "T1", TenantType.School);

        var id = await s.Create.HandleAsync(new CreateProvisionalCodedValue("CS01", "Computer Science", null, null));

        var row = s.Db.CodedValues.IgnoreQueryFilters(["Tenant"]).Single(x => x.Id == id);
        row.IsProvisional.Should().BeTrue();
        row.TenantId.Should().Be(tenant);
        row.Code.Should().Be("CS01");
    }

    [TestMethod]
    public async Task Create_ForDefaultTenant_Rejects()
    {
        using var s = new Scope(nameof(Create_ForDefaultTenant_Rejects));
        s.Tenants.Current = new TenantContext(Guid.Empty, "System", TenantType.Organization);

        var act = () => s.Create.HandleAsync(new CreateProvisionalCodedValue("CS01", "Computer Science", null, null));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Create_WhenSharedCodeExists_ThrowsCodeConflict()
    {
        using var s = new Scope(nameof(Create_WhenSharedCodeExists_ThrowsCodeConflict));
        var tenant = TenantA();
        s.Tenants.Current = new TenantContext(tenant, "T1", TenantType.School);
        await SeedSharedAsync(s.Db, "CS01", "Computer Science (global)");

        var act = () => s.Create.HandleAsync(new CreateProvisionalCodedValue("CS01", "Computer Science", null, null));

        await act.Should().ThrowAsync<CodedValueCodeConflictException>();
    }

    [TestMethod]
    public async Task List_ReturnsOnlyProvisionalValuesAcrossTenants()
    {
        using var s = new Scope(nameof(List_ReturnsOnlyProvisionalValuesAcrossTenants));
        var tenant = TenantA();
        s.Tenants.Current = new TenantContext(tenant, "T1", TenantType.School);
        var provisionalId = await s.Create.HandleAsync(new CreateProvisionalCodedValue("CS01", "Computer Science", null, null));
        // A normal (non-provisional) tenant-owned value must not appear.
        await SeedSharedAsync(s.Db, "MATH01", "Mathematics (global)");

        var results = await s.List.HandleAsync(new ListProvisionalCodedValues());

        results.Select(x => x.Id).Should().Contain(provisionalId);
        results.Select(x => x.Code).Should().NotContain("MATH01");
        var row = results.Single(x => x.Id == provisionalId);
        row.IsProvisional.Should().BeTrue();
        row.TenantId.Should().Be(tenant);
    }

    [TestMethod]
    public async Task Approve_PromotesToSharedBlueprint_VisibleToAllTenants()
    {
        using var s = new Scope(nameof(Approve_PromotesToSharedBlueprint_VisibleToAllTenants));
        var tenant = TenantA();
        s.Tenants.Current = new TenantContext(tenant, "T1", TenantType.School);
        var id = await s.Create.HandleAsync(new CreateProvisionalCodedValue("CS01", "Computer Science", null, null));

        await s.Approve.HandleAsync(new ApproveProvisionalCodedValue(id));

        // Under the default tenant filter, the value is now visible (TenantId == null).
        var visible = s.Db.CodedValues.SingleOrDefault(x => x.Id == id);
        visible.Should().NotBeNull();
        visible!.IsProvisional.Should().BeFalse();
        visible.TenantId.Should().BeNull();
    }

    [TestMethod]
    public async Task Approve_WhenSharedCodeConflictExists_ThrowsCodeConflict()
    {
        using var s = new Scope(nameof(Approve_WhenSharedCodeConflictExists_ThrowsCodeConflict));
        var tenant = TenantA();
        s.Tenants.Current = new TenantContext(tenant, "T1", TenantType.School);
        var id = await s.Create.HandleAsync(new CreateProvisionalCodedValue("CS01", "Computer Science", null, null));
        await SeedSharedAsync(s.Db, "CS01", "Computer Science (global)");

        var act = () => s.Approve.HandleAsync(new ApproveProvisionalCodedValue(id));

        await act.Should().ThrowAsync<CodedValueCodeConflictException>();
    }

    [TestMethod]
    public async Task Approve_NonProvisional_Throws()
    {
        using var s = new Scope(nameof(Approve_NonProvisional_Throws));
        var sharedId = await SeedSharedAsync(s.Db, "MATH01", "Mathematics");

        var act = () => s.Approve.HandleAsync(new ApproveProvisionalCodedValue(sharedId));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task Reject_KeepsTenantScoped_AndClearsProvisional()
    {
        using var s = new Scope(nameof(Reject_KeepsTenantScoped_AndClearsProvisional));
        var tenant = TenantA();
        s.Tenants.Current = new TenantContext(tenant, "T1", TenantType.School);
        var id = await s.Create.HandleAsync(new CreateProvisionalCodedValue("CS01", "Computer Science", null, null));

        await s.Reject.HandleAsync(new RejectProvisionalCodedValue(id));

        var row = s.Db.CodedValues.IgnoreQueryFilters(["Tenant"]).Single(x => x.Id == id);
        row.IsProvisional.Should().BeFalse();
        row.TenantId.Should().Be(tenant); // stays tenant-scoped (no hard delete)
        row.Code.Should().Be("CS01");
        // No longer pending approval.
        var pending = await s.List.HandleAsync(new ListProvisionalCodedValues());
        pending.Select(x => x.Id).Should().NotContain(id);
    }
}
