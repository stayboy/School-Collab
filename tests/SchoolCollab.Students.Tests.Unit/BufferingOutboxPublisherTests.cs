using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// adr-cross-module-calls.md outbox-atomicity follow-up: the buffering
/// publisher must NOT persist on enqueue (the handler's SaveChanges commits
/// event + entity atomically) and must flush stranded rows at disposal.
/// </summary>
[TestClass]
public class BufferingOutboxPublisherTests
{
    private static BufferingOutboxPublisher<StudentsDbContext> Create(StudentsDbContext db, IServiceProvider sp)
        => new(db, sp.GetRequiredService<ITenantProvider>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BufferingOutboxPublisher<StudentsDbContext>>.Instance);

    [TestMethod]
    public async Task EnqueueAsync_DoesNotPersist_RowCommitsWithHandlerSave()
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContextFactory<StudentsDbContext>(o => o.UseInMemoryDatabase("outbox-buffer-atomic"));
        var sp = services.BuildServiceProvider();
        await using var db = await sp.GetRequiredService<IDbContextFactory<StudentsDbContext>>().CreateDbContextAsync();
        var publisher = Create(db, sp);

        await publisher.EnqueueAsync(new CodedValueDisabled(Guid.NewGuid(), "X", DateTimeOffset.UtcNow));

        db.ChangeTracker.Entries<OutboxMessage>().Should().Contain(e => e.State == EntityState.Added);
        // Nothing persisted yet — the handler's SaveChanges commits event+entity together.
    }

    [TestMethod]
    public async Task Dispose_FlushesStrandedRows_OnlyUnsavedOnes()
    {
        var name = "outbox-buffer-disposal";
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContextFactory<StudentsDbContext>(o => o.UseInMemoryDatabase(name));
        var sp = services.BuildServiceProvider();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<StudentsDbContext>>();

        // Case 1: stranded row -> flushed by Dispose.
        var db1 = await dbFactory.CreateDbContextAsync();
        var p1 = Create(db1, sp);
        var strandedId = Guid.NewGuid();
        await p1.EnqueueAsync(new CodedValueCreated(strandedId, "A", "A", null, null, 0,
            DateTimeOffset.UtcNow, ParentCode: null, IsDisabled: false, Attributes: null, TenantId: null));
        await p1.DisposeAsync();
        (await CountRows(name)).Should().Be(1);
        await db1.DisposeAsync();

        // Case 2: committed row (handler saved) -> Dispose does NOT duplicate.
        var db2 = await dbFactory.CreateDbContextAsync();
        var p2 = Create(db2, sp);
        await p2.EnqueueAsync(new CodedValueDeleted(Guid.NewGuid(), "B", DateTimeOffset.UtcNow));
        await db2.SaveChangesAsync(); // the "handler save"
        await p2.DisposeAsync();
        var total = await CountRows(name);
        (total - 1).Should().Be(1); // exactly one more than case 1 left behind
    }

    private static async Task<int> CountRows(string dbName)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContextFactory<StudentsDbContext>(o => o.UseInMemoryDatabase(dbName));
        var sp = services.BuildServiceProvider();
        await using var db = sp.GetRequiredService<StudentsDbContext>();
        return await db.OutboxMessages.CountAsync();
    }
}
