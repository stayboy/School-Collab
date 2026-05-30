using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.DTOs;

namespace SchoolCollab.CodedValues.Core.Queries.ListRootCodedValues;

public sealed record ListRootCodedValues : IQuery<CodedValueDto[]>;
