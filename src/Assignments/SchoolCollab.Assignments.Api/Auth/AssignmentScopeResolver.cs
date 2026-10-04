using System.Security.Claims;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Constants;

namespace SchoolCollab.Assignments.Api.Auth;

/// <summary>
/// Resolves the caller's <see cref="TeacherScope"/> at the endpoint (round
/// <c>teacher-scope-auth</c> D3 / [P1-4]). The scope is resolved HERE and passed on the query —
/// a Core handler must never fetch it.
///
/// <para>Posture:</para>
/// <list type="bullet">
/// <item><c>staff</c> / <c>user-admin</c> / <c>platform-admin</c> ⇒ tenant-wide
/// (<see cref="TeacherScope.Unrestricted"/>).</item>
/// <item>otherwise <c>teacher</c> ⇒ the scope the Students port resolves; <b>EMPTY</b> when the
/// principal carries no usable <c>teacher_id</c> claim — never tenant-wide, and never widened by
/// the review-queue route's dev <c>teacherId</c> fallback.</item>
/// <item>no recognised role ⇒ tenant-wide. This is what keeps CI, the Integration suite and the
/// dev TestAuth surface (whose principal carries no role claims at all) on today's behaviour; it
/// cannot fail open in real auth, because the reader policy gates every covered read before this
/// resolver is reached, and the realm roles in D1 are what an operator assigns before the flag
/// flip.</item>
/// </list>
/// </summary>
public static class AssignmentScopeResolver
{
    public static async Task<TeacherScope> ResolveAsync(
        ClaimsPrincipal principal,
        ICurrentUser currentUser,
        ITeacherScopeProvider scopeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(scopeProvider);

        // The broader roles win: a caller who is staff AND a teacher reads tenant-wide.
        if (principal.IsInRole(RealmRoleNames.Staff)
            || principal.IsInRole(RealmRoleNames.UserAdmin)
            || principal.IsInRole(RealmRoleNames.PlatformAdmin))
        {
            return TeacherScope.Unrestricted;
        }

        if (!principal.IsInRole(RealmRoleNames.Teacher))
        {
            return TeacherScope.Unrestricted;
        }

        return currentUser.TeacherId is { } teacherId
            ? await scopeProvider.GetScopeAsync(teacherId, cancellationToken)
            : TeacherScope.Empty;
    }
}
