using Microsoft.Extensions.Logging;

namespace SchoolCollab.Auth.Services;

/// <summary>
/// Structured audit wrapper (spec §14): every admin mutation emits one structured
/// log record naming the ACTOR, the ACTION, the TARGET and the TENANT. Only
/// identifiers and role names are logged — never a token, a secret or a password.
/// </summary>
public sealed class AuthAuditLog(ILogger<AuthAuditLog> logger)
{
    /// <summary>A tenant user was created.</summary>
    public void UserCreated(string actor, string targetUserId, string tenantId)
        => Emit("user.created", actor, targetUserId, tenantId);

    /// <summary>A tenant user was updated.</summary>
    public void UserUpdated(string actor, string targetUserId, string tenantId)
        => Emit("user.updated", actor, targetUserId, tenantId);

    /// <summary>A tenant user's password was reset.</summary>
    public void PasswordReset(string actor, string targetUserId, string tenantId)
        => Emit("user.password-reset", actor, targetUserId, tenantId);

    /// <summary>A realm role was assigned to a tenant user.</summary>
    public void RoleAssigned(string actor, string targetUserId, string roleName, string tenantId)
        => Emit("role.assigned", actor, targetUserId, tenantId, roleName);

    /// <summary>A realm role was revoked from a tenant user.</summary>
    public void RoleUnassigned(string actor, string targetUserId, string roleName, string tenantId)
        => Emit("role.unassigned", actor, targetUserId, tenantId, roleName);

    private void Emit(string action, string actor, string target, string tenantId, string? role = null)
    {
        if (role is null)
        {
            logger.LogInformation(
                "Auth admin mutation: actor '{Actor}' action '{Action}' target '{Target}' tenant '{TenantId}'",
                actor, action, target, tenantId);
        }
        else
        {
            logger.LogInformation(
                "Auth admin mutation: actor '{Actor}' action '{Action}' target '{Target}' tenant '{TenantId}' role '{Role}'",
                actor, action, target, tenantId, role);
        }
    }
}
