using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Queries.ListRootCodedValues;

public sealed record ListRootCodedValues : IQuery<CodedValueDto[]>;
