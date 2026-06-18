using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListStudents;

public sealed record ListStudents : IQuery<StudentDto[]>;