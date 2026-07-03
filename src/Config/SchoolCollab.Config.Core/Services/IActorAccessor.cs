namespace SchoolCollab.Config.Core.Services;

/// <summary>
/// Provides the actor (who made a change) for audit rows. The ClaimsPrincipal
/// implementation reads the <c>sub</c> and <c>name</c> OIDC claims; the migrator
/// and tests supply a fixed system actor.
/// </summary>
public interface IActorAccessor
{
    /// <summary>The stable actor id (e.g. OIDC <c>sub</c>) or <c>system:&lt;service&gt;</c>.</summary>
    string ActorId { get; }

    /// <summary>Human-readable actor name for the audit log.</summary>
    string ActorDisplayName { get; }
}

/// <summary>
/// Default system actor used when no authenticated principal is available (e.g.
/// the migration service). Mirrors the migrator's <c>system:migrator</c> convention.
/// </summary>
public sealed class SystemActorAccessor(string actorId, string displayName) : IActorAccessor
{
    public string ActorId { get; } = actorId;
    public string ActorDisplayName { get; } = displayName;
}