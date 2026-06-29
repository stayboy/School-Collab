using SchoolCollab.Core.CQRS;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Queries.ListRootCodedValues;

public sealed record ListRootCodedValues : IQuery<CodedValueDto[]>;
