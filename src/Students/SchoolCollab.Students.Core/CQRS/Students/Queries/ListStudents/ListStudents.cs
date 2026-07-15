using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Students.Queries.ListStudents;

public sealed record ListStudents(string? Search = null) : IQuery<StudentDto[]>;