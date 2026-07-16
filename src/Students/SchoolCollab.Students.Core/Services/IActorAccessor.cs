namespace SchoolCollab.Students.Core.Services;

/// <summary>
/// Provides the actor (who made a change) for audit rows. The claims-principal
/// implementation (registered in the API host) reads the <c>sub</c> / <c>name</c>
/// OIDC claims; tests and hosts without an authenticated principal supply a
/// fixed system actor.
/// </summary>
public interface IActorAccessor
{
    /// <summary>The stable actor id (e.g. OIDC <c>sub</c>) or <c>system:&lt;service&gt;</c>.</summary>
    string ActorId { get; }

    /// <summary>Human-readable actor name for the audit log.</summary>
    string ActorDisplayName { get; }
}

/// <summary>Default system actor used when no authenticated principal is available.</summary>
public sealed class SystemActorAccessor(string actorId, string displayName) : IActorAccessor
{
    public string ActorId { get; } = actorId;
    public string ActorDisplayName { get; } = displayName;
}
