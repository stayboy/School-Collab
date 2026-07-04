using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.DisableCodedValue;

public sealed record DisableCodedValue(Guid Id) : ICommand;
