using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.GetTeacherById;

public sealed record GetTeacherById(Guid Id) : IQuery<TeacherDto?>;
