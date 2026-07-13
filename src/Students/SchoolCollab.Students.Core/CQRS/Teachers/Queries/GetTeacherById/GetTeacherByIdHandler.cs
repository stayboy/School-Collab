using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.GetTeacherById;

public sealed class GetTeacherByIdHandler(
    ITeacherRepository repository) : IQueryHandler<GetTeacherById, TeacherDto?>
{
    public async Task<TeacherDto?> HandleAsync(GetTeacherById query, CancellationToken cancellationToken = default)
    {
        var t = await repository.GetAsync(query.Id, cancellationToken);
        return t is null ? null : ToDto(t);
    }

    internal static TeacherDto ToDto(SchoolCollab.Students.Core.Domain.Teacher t) => new(
        t.Id, t.TitleCodedValueId, t.FirstName, t.LastName, t.DisplayName, t.Email, t.ContactPhone,
        t.IsDeleted, t.CreatedAt, t.UpdatedAt);
}
