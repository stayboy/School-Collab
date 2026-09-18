using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// Default <see cref="ICurrentUser"/> implementation backed by <see cref="IHttpContextAccessor"/>
/// (the authenticated <c>ClaimsPrincipal</c>) and <see cref="ITenantProvider"/>. The teacher id is
/// read from the <c>teacher_id</c> claim the same way for OIDC cookie, bearer JWT, and TestAuth
/// principals; the tenant comes from the provider's post-transformation context. No
/// <see cref="InvalidOperationException"/> is thrown from here — a missing/malformed claim simply
/// yields a null/absent result.
/// </summary>
public sealed class CurrentUser(
    IHttpContextAccessor httpContextAccessor,
    ITenantProvider tenantProvider) : ICurrentUser
{
    private ClaimsPrincipal? Principal =>
        httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated == true;

    public Guid? TeacherId
    {
        get
        {
            var principal = Principal;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var claim = principal.FindFirst("teacher_id")?.Value;
            return Guid.TryParse(claim, out var teacherId) ? teacherId : null;
        }
    }

    public TenantContext CurrentTenant => tenantProvider.GetTenantContext();
}
