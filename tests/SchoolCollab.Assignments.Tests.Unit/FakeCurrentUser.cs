using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>Test double for <see cref="ICurrentUser"/>. Default: authenticated with no
/// <c>teacher_id</c> (TeacherId = null) and a fixed tenant. Tests set
/// <see cref="TeacherId"/> to simulate a real-auth principal carrying the claim, or leave it
/// null to exercise the TestAuth/dev wire-field fallback (ar-20 P1-6).</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public bool IsAuthenticated { get; set; } = true;
    public Guid? TeacherId { get; set; }
    public TenantContext CurrentTenant { get; set; } =
        new(TenantId, "Test Tenant", TenantType.School);
}
