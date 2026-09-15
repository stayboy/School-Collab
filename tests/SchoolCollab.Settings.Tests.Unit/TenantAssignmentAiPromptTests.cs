using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.AssignmentAiPrompts.Commands.UpsertTenantAssignmentAiPrompt;
using SchoolCollab.Settings.Core.CQRS.AssignmentAiPrompts.Queries.GetTenantAssignmentAiPrompt;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// WS-B2 (spec §3.4 line 91) — the tenant-level organization AI prompt entity
/// and its get/upsert handlers. Mirrors TenantSignatureConsentTextTests.
/// </summary>
[TestClass]
public class TenantAssignmentAiPromptTests
{
    private static readonly Guid TenantId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [TestMethod]
    public void Create_FactoryAppliesTrimAndLock()
    {
        var row = TenantAssignmentAiPrompt.Create(TenantId, "  Ground questions in the district curriculum.  ", isLocked: true);

        row.TenantId.Should().Be(TenantId);
        row.SystemPrompt.Should().Be("Ground questions in the district curriculum.");
        row.IsLocked.Should().BeTrue();
    }

    [TestMethod]
    public void Create_NullPromptLeavesEmbeddedDefault()
    {
        var row = TenantAssignmentAiPrompt.Create(TenantId, systemPrompt: null);

        row.SystemPrompt.Should().BeNull();
        row.IsLocked.Should().BeFalse();
    }

    [TestMethod]
    public void SetPrompt_TrimsToNull()
    {
        var row = TenantAssignmentAiPrompt.Create(TenantId, "Initial prompt.", isLocked: true);

        row.SetPrompt("   ", isLocked: false);

        row.SystemPrompt.Should().BeNull("a whitespace-only prompt restores the embedded default");
        row.IsLocked.Should().BeFalse();
    }

    [TestMethod]
    public void SetPrompt_RejectsOver4000Chars()
    {
        var row = TenantAssignmentAiPrompt.Create(TenantId);

        var act = () => row.SetPrompt(new string('x', TenantAssignmentAiPrompt.MaxSystemPromptLength + 1), isLocked: false);

        act.Should().Throw<ArgumentException>();
    }
}

/// <summary>
/// Handler coverage for the tenant-scoped organization AI prompt row (WS-B2).
/// </summary>
[TestClass]
public class TenantAssignmentAiPromptHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private SettingsDbContext _db = default!;
    private TenantProvider _tenants = default!;
    private GetTenantAssignmentAiPromptHandler _getHandler = default!;
    private UpsertTenantAssignmentAiPromptHandler _upsertHandler = default!;

    [TestInitialize]
    public void Setup()
    {
        _tenants = new TenantProvider();
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseInMemoryDatabase($"AssignmentAiPrompt_{Guid.NewGuid()}")
            .Options;
        _db = new SettingsDbContext(options, _tenants);
        _getHandler = new GetTenantAssignmentAiPromptHandler(_db);
        _upsertHandler = new UpsertTenantAssignmentAiPromptHandler(_db, _tenants);
    }

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    private void AsTenant(Guid tenantId) =>
        _tenants.SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));

    [TestMethod]
    public async Task GetHandler_ReturnsNullWhenMissing()
    {
        AsTenant(TenantA);

        var result = await _getHandler.HandleAsync(GetTenantAssignmentAiPrompt.Instance);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task UpsertHandler_InsertsThenUpdates()
    {
        AsTenant(TenantA);

        await _upsertHandler.HandleAsync(new UpsertTenantAssignmentAiPrompt("First org prompt.", IsLocked: false));

        var afterInsert = await _getHandler.HandleAsync(GetTenantAssignmentAiPrompt.Instance);
        afterInsert.Should().NotBeNull();
        afterInsert!.SystemPrompt.Should().Be("First org prompt.");
        afterInsert.IsLocked.Should().BeFalse();

        await _upsertHandler.HandleAsync(new UpsertTenantAssignmentAiPrompt("Second org prompt.", IsLocked: true));

        var afterUpdate = await _getHandler.HandleAsync(GetTenantAssignmentAiPrompt.Instance);
        afterUpdate!.SystemPrompt.Should().Be("Second org prompt.");
        afterUpdate.IsLocked.Should().BeTrue();
        _db.TenantAssignmentAiPrompts.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task UpsertHandler_RejectsOverCap()
    {
        AsTenant(TenantA);

        var act = () => _upsertHandler.HandleAsync(new UpsertTenantAssignmentAiPrompt(
            new string('x', TenantAssignmentAiPrompt.MaxSystemPromptLength + 1), IsLocked: false));

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
