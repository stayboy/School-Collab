using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="ITeacherScopeProvider"/> (round <c>teacher-scope-auth</c> D2/Q7). Calls
/// the existing Students route <c>GET /teachers/{teacherId}/grade-assignments</c> through the
/// shared <c>students-api</c> named client (Aspire service discovery + bearer forwarding + the
/// ar-24 strict-2xx posture of the migrated registration). Mirrors
/// <see cref="TeacherDirectoryHttpClient"/>.
///
/// <para>Failure posture — fail-<b>CLOSED</b>, never fail-open: an unknown/empty teacher id, a
/// non-2xx response, a transport failure or a malformed body yields
/// <see cref="TeacherScope.Empty"/>, never <see cref="TeacherScope.Unrestricted"/>. A resolved
/// 2xx with an empty array yields a scope with an empty taught set: the taught set is then
/// <i>known</i> (the teacher teaches nothing), so the caller's own creations stay visible — the
/// alternative would hide a teacher's own drafts whenever they hold no grade rows.</para>
/// </summary>
public sealed class TeacherScopeHttpClient(
    IHttpClientFactory httpClientFactory,
    ILogger<TeacherScopeHttpClient> logger) : ITeacherScopeProvider
{
    /// <summary>Path of the Students grade-assignment read
    /// (<c>GET /teachers/{teacherId}/grade-assignments</c> — TeacherRoutes.cs).</summary>
    internal const string GradeAssignmentsPathTemplate = "teachers/{0}/grade-assignments";

    public async Task<TeacherScope> GetScopeAsync(Guid teacherId, CancellationToken cancellationToken = default)
    {
        if (teacherId == Guid.Empty)
        {
            return TeacherScope.Empty;
        }

        var students = httpClientFactory.CreateClient("students-api");
        try
        {
            using var response = await students.GetAsync(
                string.Format(GradeAssignmentsPathTemplate, teacherId), cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Teacher scope read for {TeacherId} answered {StatusCode}; failing closed to an empty scope",
                    teacherId, (int)response.StatusCode);
                return TeacherScope.Empty;
            }

            var rows = await response.Content.ReadFromJsonAsync<TeacherGradeAssignmentResponse[]>(cancellationToken);
            if (rows is null)
            {
                logger.LogWarning(
                    "Teacher scope read for {TeacherId} returned no body; failing closed to an empty scope",
                    teacherId);
                return TeacherScope.Empty;
            }

            return TeacherScope.ForTeacher(
                teacherId,
                rows.Select(r => new TeacherSubjectGrade(r.GradeLevelId, r.SubjectId, r.RoleCodedValueId)).ToList());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A client timeout (not the caller's own cancellation): fail closed.
            logger.LogWarning("Teacher scope read for {TeacherId} timed out; failing closed to an empty scope", teacherId);
            return TeacherScope.Empty;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Teacher scope read for {TeacherId} failed; failing closed to an empty scope", teacherId);
            return TeacherScope.Empty;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Teacher scope read for {TeacherId} returned a malformed body; failing closed to an empty scope", teacherId);
            return TeacherScope.Empty;
        }
        catch (NotSupportedException ex)
        {
            // A 200 carrying a non-JSON content type cannot be read as JSON.
            logger.LogWarning(ex, "Teacher scope read for {TeacherId} returned a non-JSON body; failing closed to an empty scope", teacherId);
            return TeacherScope.Empty;
        }
    }
}
