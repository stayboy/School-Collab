using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueOverride;

public sealed record RemoveCodedValueOverride(Guid GlobalCodedValueId) : ICommand;
