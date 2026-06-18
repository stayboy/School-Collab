using SchoolCollab.Students.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Queries.ListSubjects;

public sealed record ListSubjects : IQuery<SubjectDto[]>;