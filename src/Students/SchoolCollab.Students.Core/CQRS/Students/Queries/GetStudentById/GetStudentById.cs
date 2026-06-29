using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Students.Queries.GetStudentById;

public sealed record GetStudentById(Guid Id) : IQuery<StudentDto?>;