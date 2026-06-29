using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Messaging;

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
        var publisher = new OutboxIntegrationEventPublisher<FakeDbContext>(factory, logger);

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
    }

    [TestMethod]
    public async Task EnqueueAsync_ThrowsArgumentNullException_WhenMessageIsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContextFactory<FakeDbContext>(opt => opt.UseInMemoryDatabase("outbox-publisher-null"));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<FakeDbContext>>();
        var publisher = new OutboxIntegrationEventPublisher<FakeDbContext>(factory, NullLogger<OutboxIntegrationEventPublisher<FakeDbContext>>.Instance);

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
        var publisher = new OutboxIntegrationEventPublisher<FakeDbContext>(factory, NullLogger<OutboxIntegrationEventPublisher<FakeDbContext>>.Instance);

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
