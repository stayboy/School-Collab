using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RemoveCodedValueAttribute;

public sealed record RemoveCodedValueAttribute(Guid Id, string Key) : ICommand;
