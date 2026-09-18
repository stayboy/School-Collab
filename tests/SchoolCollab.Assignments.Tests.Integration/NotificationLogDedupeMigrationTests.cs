using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SchoolCollab.Assignments.Core.Domain;
using FluentAssertions;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// Discriminating test (a): the <c>DedupeNotificationLogPublishRows</c> migration collapses
/// the known F1 duplicate class — multiple <c>Publish</c> rows for the same
/// (tenant_id, assignment_id, recipient_id) — keeping the row with the <b>latest
/// activity</b> (greatest updated_at, tie-broken by attempt then id), while leaving the
/// legitimate multiple <c>Reminder</c> rows for that same key untouched. Rows are seeded
/// with raw SQL so <c>updated_at</c> / <c>attempt</c> are fully controlled.
/// </summary>
[TestClass]
public sealed class NotificationLogDedupeMigrationTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid RecipientId = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
    private static readonly Guid ContactEmail = Guid.Parse("00000000-0000-0000-0000-0000000000cc");

    [TestMethod]
    public async Task DedupeMigration_CollapsesDuplicatePublishRows_KeepsLatestActivity_AndPreservesReminderMultiplicity()
    {
        // Arrange — fresh DB, schema BEFORE the dedupe migration (no unique index yet).
        var connectionString = await AssignmentsDbFactory.CreateDatabaseAsync(Guid.NewGuid().ToString("N"));

        await using (var pre = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await pre.Database.MigrateAsync(AssignmentsDbFactory.PreDedupeMigration);
        }

        // Two duplicate Publish rows (same key): the "older" one has an earlier
        // updated_at + lower attempt; the "newer" one is the freshest re-broadcast.
        // Plus two legitimate Reminder rows (same key, kind=1 — must both survive).
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        var reminderA = Guid.NewGuid();
        var reminderB = Guid.NewGuid();

        await using (var admin = new NpgsqlConnection(connectionString))
        {
            await admin.OpenAsync();
            await InsertAsync(admin, older, Publish: true, "old-token", updatedAt: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), attempt: 0);
            await InsertAsync(admin, newer, Publish: true, "new-token-abc", updatedAt: new DateTimeOffset(2025, 1, 5, 0, 0, 0, TimeSpan.Zero), attempt: 1);
            await InsertAsync(admin, reminderA, Publish: false, "reminder-first", updatedAt: new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero), attempt: 0);
            await InsertAsync(admin, reminderB, Publish: false, "reminder-second", updatedAt: new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero), attempt: 0);
        }

        // Act — apply the dedupe migration (dedupe DELETE runs, then the partial index is created).
        await using (var ctx = AssignmentsDbFactory.CreateContext(connectionString))
        {
            await ctx.Database.MigrateAsync();
        }

        // Assert — one Publish survivor carrying the freshest payload; both Reminder rows remain.
        await using (var assert = AssignmentsDbFactory.CreateContext(connectionString))
        {
            var rows = await assert.NotificationLogs
                .IgnoreQueryFilters(["Tenant"])
                .AsNoTracking()
                .Where(x => x.TenantId == TenantId && x.AssignmentId == AssignmentId && x.RecipientId == RecipientId)
                .ToListAsync();

            var publishRows = rows.Where(r => r.Kind == NotificationKind.Publish).ToList();
            publishRows.Should().ContainSingle(
                "the dedupe migration keeps exactly the latest-activity Publish row per key");
            publishRows.Single().BodyHtml.Should().Be("new-token-abc",
                "the survivor must be the freshest activity (latest updated_at, higher attempt)");
            publishRows.Single().Id.Should().Be(newer);

            var reminderRows = rows.Where(r => r.Kind == NotificationKind.Reminder).ToList();
            reminderRows.Should().HaveCount(2,
                "the legitimate multi-Reminder rows for the same key are out of the Publish index's scope");
            reminderRows.Select(r => r.Id)
                .Should().Contain(new[] { reminderA, reminderB });
        }
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        Guid id,
        bool Publish,
        string body,
        DateTimeOffset updatedAt,
        int attempt)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO notification_logs
                (id, tenant_id, assignment_id, recipient_id, contact_id, channel, kind,
                 attempt, delivery_status, next_retry_at, to_address, subject, body_html,
                 created_at, updated_at, is_deleted, deleted_at)
            VALUES
                (@id, @tenant, @assignment, @recipient, @contact, 0,
                 @kind, @attempt, @status, @next, 'guardian@example.com', @subject, @body,
                 @created, @updated, false, NULL)
            """;
        cmd.Parameters.AddWithValue("status", (int)NotificationDeliveryStatus.Queued);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant", TenantId);
        cmd.Parameters.AddWithValue("assignment", AssignmentId);
        cmd.Parameters.AddWithValue("recipient", RecipientId);
        cmd.Parameters.AddWithValue("contact", ContactEmail);
        cmd.Parameters.AddWithValue("kind", Publish ? (int)NotificationKind.Publish : (int)NotificationKind.Reminder);
        cmd.Parameters.AddWithValue("attempt", attempt);
        cmd.Parameters.AddWithValue("next", DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue("subject", Publish ? "New assignment" : "Reminder");
        cmd.Parameters.AddWithValue("body", body);
        cmd.Parameters.AddWithValue("created", updatedAt);
        cmd.Parameters.AddWithValue("updated", updatedAt);
        await cmd.ExecuteNonQueryAsync();
    }
}
