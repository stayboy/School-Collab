using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.GetSubjectByCode;

public sealed record GetSubjectByCode(string Code) : IQuery<SubjectDto?>;