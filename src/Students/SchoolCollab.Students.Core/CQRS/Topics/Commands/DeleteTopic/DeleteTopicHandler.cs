using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands.DeleteTopic;

public sealed class DeleteTopicHandler(
    StudentsDbContext db,
    HybridCache cache,
    ILogger<DeleteTopicHandler> logger) : ICommandHandler<DeleteTopic>
{
    public async Task HandleAsync(DeleteTopic command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling DeleteTopic {Id}", command.Id);

        var topic = await db.Topics.FindAsync([command.Id], cancellationToken)
            ?? throw new TopicNotFoundException(command.Id);

        // Check for referential integrity - cannot delete if
        // student-subject assignments or grade/group bridges reference this topic.
        var hasStudentAssignments = await db.StudentSubjectAssignments
            .AnyAsync(ssa => ssa.TopicId == command.Id, cancellationToken);

        if (hasStudentAssignments)
        {
            throw new TopicReferencedException(command.Id, ["StudentSubjectAssignments"]);
        }

        var hasBridgeAssignments = await db.GradeSubjectAssignments
            .AnyAsync(gsa => gsa.TopicId == command.Id, cancellationToken);

        if (hasBridgeAssignments)
        {
            throw new TopicReferencedException(command.Id, ["GradeSubjectAssignments"]);
        }

        topic.Delete();

        db.Topics.Remove(topic);
        await db.SaveChangesAsync(cancellationToken);

        await cache.RemoveByTagAsync("students", cancellationToken);
        topic.ClearDomainEvents();

        logger.LogInformation("Topic {Id} deleted", topic.Id);
    }
}