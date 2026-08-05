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
        if (t is null) return null;
        var quals = await repository.GetQualificationCodedValueIdsAsync(query.Id, cancellationToken);
        return ToDto(t, quals);
    }

    internal static TeacherDto ToDto(SchoolCollab.Students.Core.Domain.Teacher t, Guid[] qualificationCodedValueIds) => new(
        t.Id, t.TitleCodedValueId, t.FirstName, t.LastName, t.DisplayName, t.Email, t.ContactPhone,
        t.GenderCodedValueId, t.DateOfBirth, t.LevelOfEducationCodedValueId,
        qualificationCodedValueIds,
        t.IsDeleted, t.CreatedAt, t.UpdatedAt);
}
