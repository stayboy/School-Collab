using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeachers;

public sealed record ListTeachers() : IQuery<TeacherDto[]>;
