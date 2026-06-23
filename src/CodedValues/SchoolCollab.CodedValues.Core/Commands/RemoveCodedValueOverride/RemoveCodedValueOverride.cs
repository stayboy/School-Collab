using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.RemoveCodedValueOverride;

public sealed record RemoveCodedValueOverride(Guid GlobalCodedValueId) : ICommand;
