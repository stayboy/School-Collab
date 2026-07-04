using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.DeleteCodedValue;

public sealed record DeleteCodedValue(Guid Id) : ICommand;