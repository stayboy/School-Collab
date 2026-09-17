using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-E2 (ar-16) — binding case 5: the store-driven dispatch drain.
/// <list type="bullet">
///   <item>a due <c>Queued</c> row is sent and becomes <c>Sent</c> + <c>SentAt</c>;</item>
///   <item>a send failure records <c>Attempt++</c>, the reason and <c>NextRetryAt</c> (now + backoff);</item>
///   <item>at the attempt cap the failure becomes terminal (no <c>NextRetryAt</c>).</item>
/// </list>
/// The retry re-sends the payload persisted on the row (decision (b)) — asserted
/// against what the sender actually received.
/// </summary>
[TestClass]
public class NotificationDispatchServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid RecipientId = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
    private static readonly Guid ContactId = Guid.Parse("00000000-0000-0000-0000-0000000000cc");

    private sealed class ScriptedEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public string? FailureReason { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (FailureReason is not null)
            {
                throw new Core.Domain.Exceptions.EmailDeliveryException(FailureReason);
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static (AssignmentsDbContext db, NotificationDispatchService dispatcher, ScriptedEmailSender sender) Build(string database)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(database));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        var tenants = (TenantProvider)provider.GetRequiredService<ITenantProvider>();
        tenants.SetTenant(new TenantContext(TenantId, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();

        var sender = new ScriptedEmailSender();
        var dispatcher = new NotificationDispatchService(
            db,
            provider.GetRequiredService<ITenantContextAccessor>(),
            sender,
            new LogAndSkipSmsSender(NullLogger<LogAndSkipSmsSender>.Instance),
            TimeProvider.System,
            NullLogger<NotificationDispatchService>.Instance);

        return (db, dispatcher, sender);
    }

    private static NotificationLog QueuedEmail(DateTimeOffset dueAt) =>
        NotificationLog.Queue(
            TenantId, AssignmentId, RecipientId, ContactId,
            NotificationChannel.Email, NotificationKind.Publish,
            "guardian@example.com",
            NotificationMessageBuilder.BuildPublishSubject("Math homework"),
            NotificationMessageBuilder.BuildPublishHtml("Math homework", "tok-1"),
            dueAt);

    [TestMethod]
    public async Task DueQueuedRow_IsSentAndMarkedSentWithTimestamp()
    {
        var (db, dispatcher, sender) = Build(nameof(DueQueuedRow_IsSentAndMarkedSentWithTimestamp));
        var log = QueuedEmail(DateTimeOffset.UtcNow.AddMinutes(-1));
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync();

        var sentCount = await dispatcher.DispatchPendingAsync();

        sentCount.Should().Be(1);
        log.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Sent);
        log.SentAt.Should().NotBeNull();
        log.Attempt.Should().Be(1);
        log.NextRetryAt.Should().BeNull();
        // The retry/re-send path uses exactly the payload persisted on the row.
        sender.Sent.Should().ContainSingle();
        sender.Sent[0].To.Should().Be(log.ToAddress);
        sender.Sent[0].Subject.Should().Be(log.Subject);
        sender.Sent[0].BodyHtml.Should().Be(log.BodyHtml);
    }

    [TestMethod]
    public async Task SendFailure_RecordsAttemptReasonAndNextRetry()
    {
        var (db, dispatcher, sender) = Build(nameof(SendFailure_RecordsAttemptReasonAndNextRetry));
        sender.FailureReason = "SMTP is unreachable";
        var log = QueuedEmail(DateTimeOffset.UtcNow.AddMinutes(-1));
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync();
        var before = DateTimeOffset.UtcNow;

        var sentCount = await dispatcher.DispatchPendingAsync();

        sentCount.Should().Be(0);
        log.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Failed);
        log.Attempt.Should().Be(1);
        log.FailureReason.Should().Contain("SMTP is unreachable");
        log.NextRetryAt.Should().NotBeNull();
        log.NextRetryAt!.Value.Should().BeCloseTo(before.AddMinutes(1), TimeSpan.FromMinutes(1));
    }

    [TestMethod]
    public async Task RepeatedFailureAtCap_BecomesTerminalWithNoNextRetry()
    {
        var (db, dispatcher, sender) = Build(nameof(RepeatedFailureAtCap_BecomesTerminalWithNoNextRetry));
        sender.FailureReason = "SMTP is unreachable";
        var log = QueuedEmail(DateTimeOffset.UtcNow.AddMinutes(-1));
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync();

        // Four failed attempts already recorded, the row is due again.
        for (var attempt = 0; attempt < NotificationRetrySchedule.MaxAttempts - 1; attempt++)
        {
            log.MarkFailed("previous failure", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync();
        log.Attempt.Should().Be(NotificationRetrySchedule.MaxAttempts - 1);

        await dispatcher.DispatchPendingAsync();

        log.Attempt.Should().Be(NotificationRetrySchedule.MaxAttempts);
        log.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Failed);
        log.NextRetryAt.Should().BeNull();
    }

    [TestMethod]
    public async Task NotYetDueAndSettledRows_AreNotDispatched()
    {
        var (db, dispatcher, sender) = Build(nameof(NotYetDueAndSettledRows_AreNotDispatched));
        // Not due: retry scheduled in the future.
        var future = QueuedEmail(DateTimeOffset.UtcNow.AddMinutes(30));
        future.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Queued);
        db.NotificationLogs.Add(future);

        // Terminal failure with no retry time.
        var terminal = QueuedEmail(DateTimeOffset.UtcNow.AddMinutes(-1));
        for (var attempt = 0; attempt < NotificationRetrySchedule.MaxAttempts; attempt++)
        {
            terminal.MarkFailed("gave up", null, DateTimeOffset.UtcNow);
        }
        db.NotificationLogs.Add(terminal);

        // Skipped rows are never sent.
        var skipped = QueuedEmail(DateTimeOffset.UtcNow.AddMinutes(-1));
        skipped.MarkSkipped("no deep-link token", DateTimeOffset.UtcNow);
        db.NotificationLogs.Add(skipped);

        await db.SaveChangesAsync();

        var sentCount = await dispatcher.DispatchPendingAsync();

        sentCount.Should().Be(0);
        sender.Sent.Should().BeEmpty();
        future.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Queued);
    }
}
