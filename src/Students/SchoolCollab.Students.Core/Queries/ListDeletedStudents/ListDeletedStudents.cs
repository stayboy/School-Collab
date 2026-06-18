using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListDeletedStudents;

public sealed record ListDeletedStudents : IQuery<StudentDto[]>;