namespace SchoolCollab.Core.Constants;

/// <summary>
/// The Keycloak realm roles this solution recognises — the C# spelling of
/// <c>SchoolCollab.AppHost/school-collab-realm.json</c> → <c>roles.realm</c>.
///
/// <para>One constants type, referenced from every C# site that gates on a role
/// (authorization policies, role checks). The realm import file stays literal: it is
/// Keycloak configuration that Keycloak parses, not a C# consumer — but a rename must
/// not be able to half-land on the C# side, which is exactly how <c>user-admin</c> was
/// duplicated between the realm file and <c>AuthEndpointGroup</c> before this type
/// (round <c>teacher-scope-auth</c> D1 / [P2-4]).</para>
///
/// <para>Role assignment to users stays in Keycloak / the auth admin UI — these constants
/// name the roles, they do not grant them.</para>
/// </summary>
public static class RealmRoleNames
{
    /// <summary>Administrator of a tenant's users and role assignments — the role the
    /// <c>/auth/admin/*</c> endpoints require.</summary>
    public const string UserAdmin = "user-admin";

    /// <summary>Platform-wide administrator. No route gates on it alone; it is an
    /// assignment-reader role in the teacher-scope policy.</summary>
    public const string PlatformAdmin = "platform-admin";

    /// <summary>Teacher — the read role the teacher portal's assignment scope keys off.</summary>
    public const string Teacher = "teacher";

    /// <summary>Non-teaching school staff — a tenant-wide assignment reader.</summary>
    public const string Staff = "staff";
}
