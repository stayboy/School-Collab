namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// Thrown (ar-24) when a submission-review write reaches a handler but the acting
/// teacher's tenant (resolved server-authoritatively from <c>ICurrentUser.CurrentTenant</c>)
/// does not match the target entity's tenant (<paramref name="entityTenantId"/>). A
/// cross-tenant teacher must never review or mutate another tenant's assignment/submission;
/// the API routes map this to a definitive 403 (never an unhandled 500). This is the
/// tenant-dimension counterpart of <see cref="MissingTeacherPrincipalException"/> — that
/// guards against a missing claim, this guards against a foreign-tenant claim.
/// </summary>
public sealed class TeacherTenantMismatchException : Exception
{
    public TeacherTenantMismatchException(
        Guid entityId,
        string entityKind,
        Guid teacherTenantId,
        Guid entityTenantId)
        : base($"Teacher tenant {teacherTenantId} does not match the {entityKind} {entityId} tenant {entityTenantId}; cross-tenant review is not allowed.") { }
}
