using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacher;

public sealed class DeleteTeacherHandler(
    ITeacherRepository repository,
    HybridCache cache,
    ILogger<DeleteTeacherHandler> logger) : ICommandHandler<DeleteTeacher>
{
    public async Task HandleAsync(DeleteTeacher command, CancellationToken cancellationToken = default)
    {
        var teacher = await repository.GetIncludingDeletedAsync(command.Id, cancellationToken)
            ?? throw new TeacherNotFoundException(command.Id);

        await repository.SoftDeleteAsync(teacher, cancellationToken);
        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Teacher {Id} soft-deleted", command.Id);
    }
}
