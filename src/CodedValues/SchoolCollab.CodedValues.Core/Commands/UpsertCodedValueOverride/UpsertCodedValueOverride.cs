using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.UpsertCodedValueOverride;

public sealed record UpsertCodedValueOverride(
    Guid GlobalCodedValueId,
    string? Name,
    string? Description) : ICommand;
