using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetNotificationFailures;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-E2 (ar-16) — binding case 7: the ar-17 read side. Failed rows only, and
/// tenant-scoped: a second tenant's failures for the same assignment are invisible.
/// </summary>
[TestClass]
public class GetNotificationFailuresQueryHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid ContactId = Guid.Parse("00000000-0000-0000-0000-0000000000cc");

    private static (AssignmentsDbContext db, TenantProvider tenants) Build(string database)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(database));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        var tenants = (TenantProvider)provider.GetRequiredService<ITenantProvider>();
        tenants.SetTenant(new TenantContext(TenantA, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();
        return (db, tenants);
    }

    private static NotificationLog Row(Guid tenantId, Guid contactId, NotificationDeliveryStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var log = NotificationLog.Queue(
            tenantId, AssignmentId, Guid.NewGuid(), contactId,
            NotificationChannel.Email, NotificationKind.Publish,
            "guardian@example.com", "New assignment", "<p>body</p>", now);

        switch (status)
        {
            case NotificationDeliveryStatus.Sent:
                log.MarkSent(now);
                break;
            case NotificationDeliveryStatus.Failed:
                log.MarkFailed("SMTP is unreachable", now.AddMinutes(1), now);
                break;
            case NotificationDeliveryStatus.Skipped:
                log.MarkSkipped("no deep-link token", now);
                break;
        }

        return log;
    }

    [TestMethod]
    public async Task ReturnsFailedRowsOnly_ForTheCurrentTenant()
    {
        var (db, tenants) = Build(nameof(ReturnsFailedRowsOnly_ForTheCurrentTenant));

        var failedContact = Guid.NewGuid();
        var sentContact = Guid.NewGuid();
        var skippedContact = Guid.NewGuid();
        db.NotificationLogs.AddRange(
            Row(TenantA, failedContact, NotificationDeliveryStatus.Failed),
            Row(TenantA, sentContact, NotificationDeliveryStatus.Sent),
            Row(TenantA, skippedContact, NotificationDeliveryStatus.Skipped));
        await db.SaveChangesAsync();

        // A different tenant's failure on the SAME assignment.
        tenants.SetTenant(new TenantContext(TenantB, "SchoolB", TenantType.School));
        db.NotificationLogs.Add(Row(TenantB, Guid.NewGuid(), NotificationDeliveryStatus.Failed));
        await db.SaveChangesAsync();

        tenants.SetTenant(new TenantContext(TenantA, "SchoolA", TenantType.School));
        var handler = new GetNotificationFailuresHandler(db, NullLogger<GetNotificationFailuresHandler>.Instance);

        var result = await handler.HandleAsync(new GetNotificationFailures(AssignmentId));

        var failure = result.Should().ContainSingle().Subject;
        failure.ContactId.Should().Be(failedContact);
        failure.Channel.Should().Be(ContactChannelDto.Email);
        failure.Kind.Should().Be(NotificationKindDto.Publish);
        failure.Attempt.Should().Be(1);
        failure.FailureReason.Should().Contain("SMTP is unreachable");
        failure.NextRetryAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task UnknownAssignment_ReturnsEmpty()
    {
        var (db, _) = Build(nameof(UnknownAssignment_ReturnsEmpty));
        db.NotificationLogs.Add(Row(TenantA, ContactId, NotificationDeliveryStatus.Failed));
        await db.SaveChangesAsync();

        var handler = new GetNotificationFailuresHandler(db, NullLogger<GetNotificationFailuresHandler>.Instance);

        var result = await handler.HandleAsync(new GetNotificationFailures(Guid.NewGuid()));

        result.Should().BeEmpty();
    }
}
