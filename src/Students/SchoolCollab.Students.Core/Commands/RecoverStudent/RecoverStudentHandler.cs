using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.Commands.RecoverStudent;

public sealed class RecoverStudentHandler(
    IStudentRepository repository,
    HybridCache cache,
    ILogger<RecoverStudentHandler> logger) : ICommandHandler<RecoverStudent>
{
    public async Task HandleAsync(RecoverStudent command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Handling RecoverStudent {Id}", command.Id);

        var student = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new StudentNotFoundException(command.Id);

        if (!student.IsDeleted)
            return;

        student.Recover();

        try
        {
            await repository.UpdateAsync(student, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Student", student.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);

        logger.LogInformation("Student {Id} recovered", student.Id);
    }
}