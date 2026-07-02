using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Config.Core.Data;
using SchoolCollab.Config.Core.Domain;
using SchoolCollab.Config.Core.Services;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Config.Tests.Unit;

[TestClass]
public class FeatureFlagAuditorTests : IDisposable
{
    private readonly ConfigDbContext _db;

    public FeatureFlagAuditorTests()
    {
        var options = new DbContextOptionsBuilder<ConfigDbContext>()
            .UseInMemoryDatabase($"AuditTest_{Guid.NewGuid()}")
            .Options;
        _db = new ConfigDbContext(options, new DesignTimeTenantProvider());
    }

    [TestMethod]
    public async Task Record_adds_audit_row_with_actor_and_before_after()
    {
        var auditor = new FeatureFlagAuditor(new SystemActorAccessor("system:test", "Test"));
        var flag = FeatureFlag.Create("FEATURE:X", "X", null, true);

        _db.FeatureFlags.Add(flag);
        auditor.Record(_db, tenantId: null, flag.Id, flag.Key,
            FlagChangeKind.Disabled, previousIsEnabled: true, newIsEnabled: false, reason: "turn off");
        await _db.SaveChangesAsync();

        _db.FlagAuditEntries.Should().ContainSingle();
        var entry = _db.FlagAuditEntries.Single();
        entry.ChangeKind.Should().Be(FlagChangeKind.Disabled);
        entry.PreviousIsEnabled.Should().BeTrue();
        entry.NewIsEnabled.Should().BeFalse();
        entry.Reason.Should().Be("turn off");
        entry.ActorId.Should().Be("system:test");
        entry.ActorDisplayName.Should().Be("Test");
        entry.FeatureFlagKey.Should().Be("FEATURE:X");
    }

    public void Dispose() => _db.Dispose();
}