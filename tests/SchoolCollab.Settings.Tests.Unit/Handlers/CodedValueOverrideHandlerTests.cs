using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueOverride;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.UpsertCodedValueOverride;
using SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.GetCodedValueById;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

[TestClass]
public class CodedValueOverrideHandlerTests
{
    private sealed class Scope : IDisposable
    {
        public SettingsDbContext Db { get; }
        public HybridCache Cache { get; }
        public ITenantProvider Tenants { get; } = new TenantProvider();
        public GetCodedValueByIdHandler Resolver { get; }
        public UpsertCodedValueOverrideHandler Upsert { get; }
        public RemoveCodedValueOverrideHandler Remove { get; }

        public Scope(string dbName)
        {
            Db = BuildDb(dbName);
            Cache = BuildCache();
            // The TenantProvider defaults to a System/Empty tenant — consistent for
            // both the write (override row) and the read (override resolution).
            Resolver = new GetCodedValueByIdHandler(Db, Cache);
            Upsert = new UpsertCodedValueOverrideHandler(Db, Tenants, Cache, Resolver);
            Remove = new RemoveCodedValueOverrideHandler(Db, Tenants, Cache);
        }

        public void Dispose() => Db.Dispose();

        private static SettingsDbContext BuildDb(string name)
        {
            var services = new ServiceCollection();
            services.AddTenancy();
            services.AddDbContext<SettingsDbContext>(o => o.UseInMemoryDatabase(name));
            return services.BuildServiceProvider().GetRequiredService<SettingsDbContext>();
        }

        private static HybridCache BuildCache()
        {
            var services = new ServiceCollection();
            services.AddDistributedMemoryCache();
            services.AddHybridCache();
            return services.BuildServiceProvider().GetRequiredService<HybridCache>();
        }
    }

    private static async Task<Guid> SeedCodedValueAsync(SettingsDbContext db, string code, string name)
    {
        var cv = CodedValue.Create(code, name, null, null, 0);
        db.CodedValues.Add(cv);
        await db.SaveChangesAsync();
        return cv.Id;
    }

    [TestMethod]
    public async Task Upsert_CreatesOverride_AndReturnsResolvedDto()
    {
        using var s = new Scope("override-create");
        var id = await SeedCodedValueAsync(s.Db, "GRADE_1", "Grade 1");

        var dto = await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Standard 1", null));

        dto.Name.Should().Be("Standard 1");
        // The override row exists for the current (System) tenant.
        s.Db.TenantCodedValueOverrides.Should().ContainSingle(o => o.GlobalCodedValueId == id);
    }

    [TestMethod]
    public async Task Upsert_UpdatesExistingOverride()
    {
        using var s = new Scope("override-update");
        var id = await SeedCodedValueAsync(s.Db, "GRADE_2", "Grade 2");

        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "First", null));
        var dto = await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Second", null));

        dto.Name.Should().Be("Second");
        s.Db.TenantCodedValueOverrides.Should().ContainSingle(o => o.GlobalCodedValueId == id);
    }

    [TestMethod]
    public async Task Remove_FallsBackToBlueprintName()
    {
        using var s = new Scope("override-remove");
        var id = await SeedCodedValueAsync(s.Db, "GRADE_3", "Grade 3");

        await s.Upsert.HandleAsync(new UpsertCodedValueOverride(id, "Standard 3", null));
        await s.Remove.HandleAsync(new RemoveCodedValueOverride(id));

        s.Db.TenantCodedValueOverrides.Should().BeEmpty();

        var resolved = await s.Resolver.HandleAsync(new GetCodedValueById(id));
        resolved.Should().NotBeNull();
        resolved!.Name.Should().Be("Grade 3"); // back to the global blueprint name
    }

    [TestMethod]
    public async Task Remove_WhenNoOverride_IsIdempotent()
    {
        using var s = new Scope("override-remove-nop");
        var id = await SeedCodedValueAsync(s.Db, "GRADE_4", "Grade 4");

        // Removing a non-existent override must not throw.
        var act = async () => await s.Remove.HandleAsync(new RemoveCodedValueOverride(id));
        await act.Should().NotThrowAsync();
    }

    [TestMethod]
    public async Task Upsert_ForMissingCodedValue_ThrowsNotFound()
    {
        using var s = new Scope("override-missing");
        var act = async () => await s.Upsert.HandleAsync(
            new UpsertCodedValueOverride(Guid.NewGuid(), "X", null));
        await act.Should().ThrowAsync<CodedValueNotFoundException>();
    }
}