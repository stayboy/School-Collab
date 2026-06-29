using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Queries.GetSubjectById;

public sealed record GetSubjectById(Guid Id) : IQuery<SubjectDto?>;