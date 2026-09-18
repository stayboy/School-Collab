using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// Server-authoritative view of the authenticated principal (ar-20). Resolves the
/// acting teacher id and tenant from the <see cref="System.Security.Claims.ClaimsPrincipal"/>
/// regardless of which authentication scheme produced it — OIDC cookie, bearer JWT, or
/// TestAuth. Consumption sites (assignment attribution handlers) use this instead of
/// trusting the wire-request identity fields, so write attribution can no longer be
/// spoofed by the client. When no teacher claim is present (TestAuth/dev default), the
/// handlers fall back to the request's identity field.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Whether the current request carries an authenticated principal.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// The acting teacher id, resolved from the <c>teacher_id</c> claim. Null when the
    /// principal carries no <c>teacher_id</c> claim (e.g. the TestAuth default posture),
    /// or when the current request is unauthenticated. Never throws on a malformed claim.
    /// </summary>
    Guid? TeacherId { get; }

    /// <summary>
    /// The resolved tenant context for the current request (post
    /// <see cref="TenantClaimsTransformation"/>, falling back to the principal's
    /// <c>tenant_id</c> claim). Returns the default System context when no tenant is in scope.
    /// </summary>
    TenantContext CurrentTenant { get; }
}
