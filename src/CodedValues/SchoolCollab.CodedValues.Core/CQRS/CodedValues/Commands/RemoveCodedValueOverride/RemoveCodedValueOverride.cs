using SchoolCollab.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.RemoveCodedValueOverride;

public sealed record RemoveCodedValueOverride(Guid GlobalCodedValueId) : ICommand;
