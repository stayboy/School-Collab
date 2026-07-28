using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.EntityCodes.Commands.ActivateEntityCodeRule;

/// <summary>
/// Activates an <see cref="Domain.EntityCodeRule"/>, deactivating any other
/// active rule for the same entity-type scope. There can be only one active
/// rule per (Code, TenantId) at a time — spec §3.1.
/// </summary>
public sealed record ActivateEntityCodeRule(Guid Id) : ICommand;