using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Queries.GetSubjectByCode;

public sealed record GetSubjectByCode(string Code) : IQuery<SubjectDto?>;