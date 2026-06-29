using SchoolCollab.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.UpsertCodedValueOverride;

public sealed record UpsertCodedValueOverride(
    Guid GlobalCodedValueId,
    string? Name,
    string? Description) : ICommand;
