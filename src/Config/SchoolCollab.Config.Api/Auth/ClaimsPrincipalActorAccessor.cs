using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SchoolCollab.Config.Core.Services;

namespace SchoolCollab.Config.Api.Auth;

/// <summary>
/// Reads the audit actor from the current authenticated <see cref="HttpContext"/>
/// user. Prefers the OIDC <c>sub</c> claim and falls back to
/// <see cref="ClaimTypes.NameIdentifier"/> (which is what <c>TestAuthHandler</c>
/// populates in integration tests) so the actor id is captured in both
/// production and test contexts. The display name prefers <c>name</c>, then
/// <see cref="ClaimTypes.Name"/>, then the actor id.
/// </summary>
public sealed class ClaimsPrincipalActorAccessor(IHttpContextAccessor httpContextAccessor) : IActorAccessor
{
    public string ActorId =>
        httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value
        ?? httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? "anonymous";

    public string ActorDisplayName =>
        httpContextAccessor.HttpContext?.User?.FindFirst("name")?.Value
        ?? httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value
        ?? ActorId;
}