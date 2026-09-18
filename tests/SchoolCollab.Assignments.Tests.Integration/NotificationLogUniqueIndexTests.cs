using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Notifications;
using FluentAssertions;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// Discriminating test (b): the partial unique index
/// <c>ux_notification_logs_publish_uniqueness</c> rejects a second <c>Publish</c> row for
/// the same (tenant_id, assignment_id, recipient_id) key with a unique-constraint
/// violation, while a second <c>Reminder</c> row on that same key is allowed (the index
/// is partial — <c>WHERE kind = 0</c> exclusively). Requires real Postgres because the
/// in-memory EF provider does not enforce unique indexes.
/// </summary>
[TestClass]
public sealed class NotificationLogUniqueIndexTests
{
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-000000000011");
    private static readonly Guid RecipientId = Guid.Parse("00000000-0000-0000-0000-000000000022");
    private static readonly Guid ContactEmail = Guid.Parse("00000000-0000-0000-0000-000000000033");

    [TestMethod]
    public async Task SecondPublishRow_ForSameKey_ThrowsUniqueConstraintViolation()
    {
        var connectionString = await AssignmentsDbFactory.CreateDatabaseAsync(Guid.NewGuid().ToString("N"));
        await using (var ctx = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await ctx.Database.MigrateAsync();
        }

        await using (var first = AssignmentsDbFactory.CreateContext(connectionString, tenantId: TenantId))
        {
            first.NotificationLogs.Add(Build(NotificationKind.Publish, body: "first-publish"));
            await first.SaveChangesAsync();
        }

        await using var second = AssignmentsDbFactory.CreateContext(connectionString, tenantId: TenantId);
        second.NotificationLogs.Add(Build(NotificationKind.Publish, body: "second-publish"));

        // The second Publish INSERT for the same key must violate the partial unique index.
        await FluentActions
            .Awaiting(() => second.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateException>();
    }

    [TestMethod]
    public async Task SecondReminderRow_ForSameKey_IsAllowed()
    {
        var connectionString = await AssignmentsDbFactory.CreateDatabaseAsync(Guid.NewGuid().ToString("N"));
        await using (var ctx = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await ctx.Database.MigrateAsync();
        }

        await using (var first = AssignmentsDbFactory.CreateContext(connectionString, tenantId: TenantId))
        {
            first.NotificationLogs.Add(Build(NotificationKind.Reminder, body: "reminder-1"));
            await first.SaveChangesAsync();
        }

        await using var second = AssignmentsDbFactory.CreateContext(connectionString, tenantId: TenantId);
        second.NotificationLogs.Add(Build(NotificationKind.Reminder, body: "reminder-2"));
        await second.SaveChangesAsync(); // must NOT throw — Reminder is outside the partial index

        var count = await second.NotificationLogs
            .IgnoreQueryFilters(["Tenant"])
            .AsNoTracking()
            .CountAsync(x => x.TenantId == TenantId
                && x.AssignmentId == AssignmentId
                && x.RecipientId == RecipientId
                && x.Kind == NotificationKind.Reminder);

        count.Should().Be(2, "the partial index only constrains the Publish kind");
    }

    private static NotificationLog Build(NotificationKind kind, string body) =>
        NotificationLog.Queue(
            TenantId, AssignmentId, RecipientId, ContactEmail,
            NotificationChannel.Email, kind,
            "guardian@example.com",
            kind == NotificationKind.Publish ? "New assignment" : "Reminder",
            body,
            DateTimeOffset.UtcNow);
}
