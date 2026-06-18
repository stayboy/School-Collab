using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.GetStudentByStudentNumber;

public sealed record GetStudentByStudentNumber(string StudentNumber) : IQuery<StudentDto?>;