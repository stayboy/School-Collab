using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.DeleteEntityCodeRule;

/// <summary>Soft-deletes an <see cref="Domain.EntityCodeRule"/>.</summary>
public sealed record DeleteEntityCodeRule(Guid Id) : ICommand;