using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.UpdateTeacher;

public sealed class UpdateTeacherHandler(
    ITeacherRepository repository,
    HybridCache cache,
    ILogger<UpdateTeacherHandler> logger) : ICommandHandler<UpdateTeacher>
{
    public async Task HandleAsync(UpdateTeacher command, CancellationToken cancellationToken = default)
    {
        var teacher = await repository.GetAsync(command.Id, cancellationToken)
            ?? throw new TeacherNotFoundException(command.Id);

        teacher.Update(command.FirstName, command.LastName, command.DisplayName,
            command.GenderCodedValueId, command.DateOfBirth, command.LevelOfEducationCodedValueId);
        await repository.UpdateAsync(teacher, cancellationToken);

        var requested = (command.QualificationCodedValueIds ?? []).ToHashSet();
        var existing = (await repository.GetQualificationCodedValueIdsAsync(command.Id, cancellationToken)).ToHashSet();
        foreach (var q in requested.Except(existing))
            await repository.AddQualificationAsync(TeacherQualification.Create(command.Id, q), cancellationToken);
        foreach (var q in existing.Except(requested))
            await repository.RemoveQualificationAsync(command.Id, q, cancellationToken);

        await cache.RemoveByTagAsync("teachers", cancellationToken);

        logger.LogInformation("Teacher {Id} updated", command.Id);
    }
}
