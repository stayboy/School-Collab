using SchoolCollab.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.DisableCodedValue;

public sealed record DisableCodedValue(Guid Id) : ICommand;
