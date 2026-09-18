namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// Thrown (ar-20, P1-6) when a write reaches a handler in real-auth mode (OIDC/bearer
/// active — <c>FEATURE:DisableOIDCAuth=false</c>) but the authenticated principal carries
/// no <c>teacher_id</c> claim, so attribution cannot be resolved server-authoritatively.
/// In real-auth the wire identity field is NEVER honored; the operation is rejected with
/// this typed exception instead of a bare <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class MissingTeacherPrincipalException : Exception
{
    public MissingTeacherPrincipalException(string operation)
        : base($"The authenticated principal did not provide a teacher_id claim required for server-authoritative attribution of '{operation}'.") { }
}
