using System.Net;
using System.Net.Http.Json;

namespace SchoolCollab.Admin.Shared.Services;

// Local wire mirrors of the three Students lookups the template constraint editor offers. They are
// MIRRORS, not shared contracts: Settings.Application may not reference Students.Application (the
// bounded-context rule), exactly like the NotificationTemplateApiClient's mirrors of Settings.Core
// DTOs. Each mirrors only the fields the picker reads, and the client's wire tests pin the three
// paths + shapes so mirror drift fails a test instead of silently mis-matching a template.

/// <summary>
/// One picker option: the reference id the matcher compares plus its display name.
/// <paramref name="IsActive"/> mirrors the activity-group listing's own flag (null for listings
/// that have none) so an archived group is never offered as a constraint — it could never match.
/// </summary>
public sealed record LookupOptionDto(Guid Id, string Name, bool? IsActive = null);

/// <summary>
/// Read-only cross-context lookup client for the template constraint editor's pickers (spec §3.10,
/// round notification-templates-b3b). The constraint <c>Value</c> encodings are the matcher's own
/// (<c>AssignmentTemplateContext</c>): a grade level's Students id, a topic id, an activity group's
/// id — none of which the Settings module can resolve from its own codebook, because the matcher
/// compares assignment target reference ids, not coded-value ids.
/// <para>
/// An originator/enrichment read under <c>adr-cross-module-calls.md</c>: it degrades. Each method
/// returns <b>null</b> when the route is not mapped for this host (404 — the
/// <c>FEATURE:EnableActivityGroups</c> posture, the same convention as
/// <c>AssignmentsApiClient.GetLinkedActivityGroupIdsAsync</c>) and throws on any other failure, so
/// the caller can tell "this school does not serve that" apart from "the lookup is down". The
/// editor keeps rendering existing constraints' raw values either way (an edit round-trips without
/// data loss) and offers only the dimensions it can actually fill — never a silently empty picker,
/// never a save that drops constraints because the picker had no options.
/// </para>
/// </summary>
public sealed class StudentsLookupClient(HttpClient http)
{
    /// <summary>Grade levels, ordered by the Students API (the matcher's GradeLevel reference ids).
    /// Null when the route is not mapped for this host.</summary>
    public Task<IReadOnlyList<LookupOptionDto>?> ListGradeLevelsAsync(CancellationToken ct = default) =>
        GetOptionsAsync("/students/grade-levels", ct);

    /// <summary>Topics/subjects (the matcher's Subject reference ids — <c>Assignment.TopicId</c>).
    /// Null when the route is not mapped for this host.</summary>
    public Task<IReadOnlyList<LookupOptionDto>?> ListTopicsAsync(CancellationToken ct = default) =>
        GetOptionsAsync("/students/topics", ct);

    /// <summary>Activity groups (the matcher's ActivityGroup reference ids). Null when the route is
    /// not mapped — the live case while <c>FEATURE:EnableActivityGroups</c> is off, because
    /// <c>StudentEndpoints</c> maps <c>/activity-groups</c> only when the flag is on.</summary>
    public Task<IReadOnlyList<LookupOptionDto>?> ListActivityGroupsAsync(CancellationToken ct = default) =>
        GetOptionsAsync("/activity-groups", ct);

    private async Task<IReadOnlyList<LookupOptionDto>?> GetOptionsAsync(string path, CancellationToken ct)
    {
        var response = await http.GetAsync(path, ct);

        // 404 = "no endpoint matched", i.e. the route is not mapped for this host. That is a feature
        // posture (the flag above), not a fault, so it is reported as null rather than thrown: the
        // caller must not dress a school that simply does not have the feature as an outage.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        // Unknown members are ignored by default, so a wider Students DTO deserializes into this
        // mirror as long as the two read fields keep their names.
        var options = await response.Content.ReadFromJsonAsync<List<LookupOptionDto>>(cancellationToken: ct);
        return options ?? [];
    }
}
