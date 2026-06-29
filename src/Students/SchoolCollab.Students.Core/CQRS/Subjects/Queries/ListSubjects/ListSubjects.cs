using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Queries.ListSubjects;

public sealed record ListSubjects : IQuery<SubjectDto[]>;