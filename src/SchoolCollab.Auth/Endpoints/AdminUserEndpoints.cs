using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Endpoints;

/// <summary>
/// <c>/auth/admin/users*</c> — realm user administration over <see cref="KeycloakAdminClient"/>
/// (spec §13). The <c>user-admin</c> role gate is applied at group level by
/// <see cref="AuthEndpointGroup"/>. Every mutation emits one <see cref="AuthAuditLog"/> record
/// (spec §14) naming the actor (the acting teacher id resolved from the principal's
/// <c>teacher_id</c> claim — the same claim <c>CurrentUser</c> reads), the
/// target and the tenant — never a credential; the password-reset path in particular never logs,
/// echoes or throws the new password.
/// </summary>
public static class AdminUserEndpoints
{
    /// <summary>Body for the credential reset. The value is passed straight to the Admin REST
    /// client and must never reach a log line, a response body or an exception message.</summary>
    public sealed record ResetPasswordRequest(string NewPassword);

    public static IEndpointRouteBuilder MapAdminUserRoutes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/users");

        // GET /auth/admin/users — realm users, with the client's supported username/email filters.
        group.MapGet("", async (
            KeycloakAdminClient admin,
            CancellationToken ct,
            string? username = null,
            string? email = null) =>
        {
            var result = await admin.ListUsersAsync(username, email, ct);
            return result.Status switch
            {
                AdminStatus.Success => Results.Ok(result.Users),
                _ => AuthEndpointGroup.AdminFailure(result.Status, result.Detail),
            };
        });

        // GET /auth/admin/users/{userId}
        group.MapGet("/{userId}", async (string userId, KeycloakAdminClient admin, CancellationToken ct) =>
        {
            var result = await admin.GetUserAsync(userId, ct);
            return result.Status switch
            {
                AdminStatus.Success => result.User is null ? Results.NotFound() : Results.Ok(result.User),
                _ => AuthEndpointGroup.AdminFailure(result.Status, result.Detail),
            };
        });

        // POST /auth/admin/users — create a realm user (the body is the user representation).
        group.MapPost("", async (
            KeycloakUser request,
            HttpContext httpContext,
            AuthAuditLog audit,
            KeycloakAdminClient admin,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username))
            {
                return Results.Json(new { error = "username_required" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await admin.CreateUserAsync(request, ct);
            if (!result.IsSuccess)
            {
                return AuthEndpointGroup.AdminFailure(result.Status, result.Detail);
            }

            // The audit target is the created username — the handle the caller supplied, and the
            // one an operator searches the trail by. The realm id Keycloak minted (read off the
            // 201 Location by the Admin REST client) is surfaced in the response body instead.
            audit.UserCreated(
                AuthEndpointGroup.AuditActor(httpContext.User),
                request.Username,
                AuthEndpointGroup.AuditTenant(httpContext.User));
            return Results.Json(
                new { id = result.UserId, status = "created" },
                statusCode: StatusCodes.Status201Created);
        });

        // PUT /auth/admin/users — full-representation update. The body IS the complete user
        // representation, carrying the user id and the claim attributes (Keycloak's own PUT
        // semantics — the client resolves the target by user.Id, so the id must be present here,
        // and the attributes in the body are what the user's attributes become).
        group.MapPut("", async (
            KeycloakUser request,
            HttpContext httpContext,
            AuthAuditLog audit,
            KeycloakAdminClient admin,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Id))
            {
                return Results.Json(new { error = "user_id_required" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await admin.UpdateUserAsync(request, ct);
            if (!result.IsSuccess)
            {
                return AuthEndpointGroup.AdminFailure(result.Status, result.Detail);
            }

            audit.UserUpdated(
                AuthEndpointGroup.AuditActor(httpContext.User),
                request.Id,
                AuthEndpointGroup.AuditTenant(httpContext.User));
            return Results.Json(new { status = "updated" });
        });

        // PUT /auth/admin/users/{userId}/reset-password
        group.MapPut("/{userId}/reset-password", async (
            string userId,
            ResetPasswordRequest request,
            HttpContext httpContext,
            AuthAuditLog audit,
            KeycloakAdminClient admin,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return Results.Json(new { error = "password_required" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await admin.ResetPasswordAsync(userId, request.NewPassword, ct);
            if (!result.IsSuccess)
            {
                return AuthEndpointGroup.AdminFailure(result.Status, result.Detail);
            }

            audit.PasswordReset(
                AuthEndpointGroup.AuditActor(httpContext.User),
                userId,
                AuthEndpointGroup.AuditTenant(httpContext.User));
            return Results.NoContent();
        });

        return app;
    }
}
