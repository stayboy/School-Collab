using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.CQRS;
using SchoolCollab.Assignments.Core.Data;

namespace SchoolCollab.Assignments.Core.Messaging;

public sealed class OutboxIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly AssignmentsDbContext _db;

    public OutboxIntegrationEventPublisher(AssignmentsDbContext db) => _db = db;

    public async Task PublishAsync(object payload, CancellationToken ct = default)
    {
        var message = new OutboxMessage
        {
            Type = payload.GetType().Name,
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _db.OutboxMessages.AddAsync(message, ct);
    }
}