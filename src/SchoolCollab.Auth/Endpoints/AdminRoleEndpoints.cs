using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Endpoints;

/// <summary>
/// <c>/auth/admin/roles</c> and <c>/auth/admin/users/{userId}/role-mappings</c> — realm-roles read
/// and role assignment / revocation over <see cref="KeycloakAdminClient"/>. The <c>user-admin</c>
/// gate is applied at group level by <see cref="AuthEndpointGroup"/>. Grants and revocations are
/// audit-logged **per role** (the audit API is per-role-name), naming actor, target and tenant.
/// </summary>
public static class AdminRoleEndpoints
{
    /// <summary>The {id, name} role pairs a grant/revocation carries. The id must be resolved via
    /// <c>GET /auth/admin/roles</c> — Keycloak's role-mappings endpoint keys on role ids, and the
    /// name alone is not sufficient (the client documents this on <see cref="KeycloakRole"/>).</summary>
    public sealed record RoleMappingRequest(IReadOnlyList<KeycloakRole> Roles);

    public static IEndpointRouteBuilder MapAdminRoleRoutes(this IEndpointRouteBuilder app)
    {
        // GET /auth/admin/roles — list realm roles (id + name) so a caller can resolve ids first.
        app.MapGet("/roles", async (KeycloakAdminClient admin, CancellationToken ct) =>
        {
            var result = await admin.ListRealmRolesAsync(ct);
            return result.Status switch
            {
                AdminStatus.Success => Results.Ok(result.Roles),
                _ => AuthEndpointGroup.AdminFailure(result.Status, result.Detail),
            };
        });

        var group = app.MapGroup("/users/{userId}/role-mappings");

        // POST /auth/admin/users/{userId}/role-mappings — grant realm roles. POST (not the plan
        // sketch's PUT) on purpose: Keycloak's own role-mappings contract is POST/DELETE to the
        // same path, and that is exactly what the client mirrors.
        group.MapPost("", async (
            string userId,
            [FromBody] RoleMappingRequest request,
            HttpContext httpContext,
            AuthAuditLog audit,
            KeycloakAdminClient admin,
            CancellationToken ct) =>
        {
            if (request.Roles.Count == 0)
            {
                return Results.Json(new { error = "roles_required" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await admin.AssignRealmRolesAsync(userId, request.Roles, ct);
            if (!result.IsSuccess)
            {
                return AuthEndpointGroup.AdminFailure(result.Status, result.Detail);
            }

            foreach (var role in request.Roles)
            {
                audit.RoleAssigned(
                    AuthEndpointGroup.AuditActor(httpContext.User),
                    userId,
                    role.Name,
                    AuthEndpointGroup.AuditTenant(httpContext.User));
            }

            return Results.NoContent();
        });

        // DELETE /auth/admin/users/{userId}/role-mappings — revoke realm roles. [FromBody] is
        // required here: complex types are not body-INFERRED on DELETE (the router asks for an
        // explicit binding for body-bound parameters on non-body-friendly methods).
        group.MapDelete("", async (
            string userId,
            [FromBody] RoleMappingRequest request,
            HttpContext httpContext,
            AuthAuditLog audit,
            KeycloakAdminClient admin,
            CancellationToken ct) =>
        {
            if (request.Roles.Count == 0)
            {
                return Results.Json(new { error = "roles_required" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await admin.UnassignRealmRolesAsync(userId, request.Roles, ct);
            if (!result.IsSuccess)
            {
                return AuthEndpointGroup.AdminFailure(result.Status, result.Detail);
            }

            foreach (var role in request.Roles)
            {
                audit.RoleUnassigned(
                    AuthEndpointGroup.AuditActor(httpContext.User),
                    userId,
                    role.Name,
                    AuthEndpointGroup.AuditTenant(httpContext.User));
            }

            return Results.NoContent();
        });

        return app;
    }
}
