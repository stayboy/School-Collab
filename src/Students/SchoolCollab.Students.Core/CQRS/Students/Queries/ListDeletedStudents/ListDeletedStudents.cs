using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Students.Queries.ListDeletedStudents;

public sealed record ListDeletedStudents : IQuery<StudentDto[]>;