using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.GetStudentById;

public sealed record GetStudentById(Guid Id) : IQuery<StudentDto?>;