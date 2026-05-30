using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.DisableCodedValue;

public sealed record DisableCodedValue(Guid Id) : ICommand;
