using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.GetSubjectById;

public sealed record GetSubjectById(Guid Id) : IQuery<SubjectDto?>;