using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tests.Unit.Messaging;

[TestClass]
public class OutboxIntegrationEventPublisherTests
{
    [TestMethod]
    public async Task EnqueueAsync_PersistsOutboxRow_WithExpectedShape()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContextFactory<FakeDbContext>(opt => opt.UseInMemoryDatabase("outbox-publisher-test"));
        await using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IDbContextFactory<FakeDbContext>>();
        var logger = NullLogger<OutboxIntegrationEventPublisher<FakeDbContext>>.Instance;
        // Default tenant (Guid.Empty) → TenantId stamped as null (global event).
        var tenants = new DesignTimeTenantProvider();
        var publisher = new OutboxIntegrationEventPublisher<FakeDbContext>(factory, tenants, logger);

        var payload = new TestEvent("hello");

        // Act
        await publisher.EnqueueAsync(payload);

        // Assert
        await using var dbContext = await factory.CreateDbContextAsync();
        var row = await dbContext.Set<OutboxMessage>().SingleAsync();
        Assert.AreEqual(typeof(TestEvent).FullName, row.Type);
        Assert.IsFalse(string.IsNullOrEmpty(row.Payload));
        Assert.IsTrue(row.Payload.Contains("hello"));
        Assert.IsTrue(row.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.AreEqual(0, row.Attempts);
        Assert.IsNull(row.DispatchedAt);
        Assert.IsNull(row.LastError);
        // FR-15: Guid.Empty context → null TenantId (global event).
        Assert.IsNull(row.TenantId);
    }

    [TestMethod]
    public async Task EnqueueAsync_ThrowsArgumentNullException_WhenMessageIsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContextFactory<FakeDbContext>(opt => opt.UseInMemoryDatabase("outbox-publisher-null"));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<FakeDbContext>>();
        var tenants = new DesignTimeTenantProvider();
        var publisher = new OutboxIntegrationEventPublisher<FakeDbContext>(factory, tenants, NullLogger<OutboxIntegrationEventPublisher<FakeDbContext>>.Instance);

        // Act + Assert
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(
            async () => await publisher.EnqueueAsync<TestEvent>(null!));
    }

    [TestMethod]
    public async Task EnqueueAsync_AssignsUniqueIds_ToEachRow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContextFactory<FakeDbContext>(opt => opt.UseInMemoryDatabase("outbox-publisher-unique-ids"));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<FakeDbContext>>();
        var tenants = new DesignTimeTenantProvider();
        var publisher = new OutboxIntegrationEventPublisher<FakeDbContext>(factory, tenants, NullLogger<OutboxIntegrationEventPublisher<FakeDbContext>>.Instance);

        // Act
        await publisher.EnqueueAsync(new TestEvent("a"));
        await publisher.EnqueueAsync(new TestEvent("b"));
        await publisher.EnqueueAsync(new TestEvent("c"));

        // Assert
        await using var dbContext = await factory.CreateDbContextAsync();
        var ids = await dbContext.Set<OutboxMessage>().Select(m => m.Id).ToListAsync();
        Assert.AreEqual(3, ids.Count);
        Assert.AreEqual(3, ids.Distinct().Count());
    }

    [TestMethod]
    public async Task AC13_EnqueueAsync_UnderRealTenant_StampstenantId()
    {
        // AC-13 (FR-15): a tenant-A command enqueues an OutboxMessage → the row's
        // TenantId == A, so the dispatcher/consumer can reconstruct the tenant context.
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var services = new ServiceCollection();
        services.AddDbContextFactory<FakeDbContext>(opt => opt.UseInMemoryDatabase("outbox-publisher-tenant"));
        await using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IDbContextFactory<FakeDbContext>>();
        var tenants = new TenantProvider();
        tenants.SetTenant(new TenantContext(tenantA, "Tenant A", TenantType.School));
        var publisher = new OutboxIntegrationEventPublisher<FakeDbContext>(
            factory, tenants, NullLogger<OutboxIntegrationEventPublisher<FakeDbContext>>.Instance);

        await publisher.EnqueueAsync(new TestEvent("tenant-a-event"));

        await using var dbContext = await factory.CreateDbContextAsync();
        var row = await dbContext.Set<OutboxMessage>().SingleAsync();
        Assert.AreEqual(tenantA, row.TenantId, "FR-15: the publisher's tenant is stamped on the outbox row");
    }

    private sealed class TestEvent
    {
        public TestEvent(string message) { Message = message; }
        public string Message { get; }
    }

    private sealed class FakeDbContext : DbContext
    {
        public FakeDbContext(DbContextOptions<FakeDbContext> options) : base(options) { }

        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    }
}
