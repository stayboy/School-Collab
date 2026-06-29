using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Students.Queries.GetStudentByStudentNumber;

public sealed record GetStudentByStudentNumber(string StudentNumber) : IQuery<StudentDto?>;