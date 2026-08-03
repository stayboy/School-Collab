using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Core.Contracts;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Admin.Services;

// ── DTOs ────────────────────────────────────────────────────────────────────

public sealed record StudentDto(
    Guid Id,
    string StudentNumber,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Enriched client-side (see EnrichStudentsAsync). Left null by the API.
    int? Age = null,
    string? GenderName = null,
    // Current grade enrollment info, populated client-side from enrollments
    GradeLevelDto? CurrentGrade = null);

public sealed record GradeLevelDto(
    Guid Id,
    Guid CodedValueId,
    int Level,
    string Name,
    int DisplayOrder,
    int TopicCount,
    int StudentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? MinAge = null,
    int? MaxAge = null,
    Guid? AllowedGenderCodedValueId = null);

public sealed record GradeLevelLandingDto(
    Guid Id,
    Guid CodedValueId,
    string Name,
    int TopicCount,
    int StudentCount,
    Guid? CurrentPeriodId,
    string? CurrentPeriodName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Enrollment validation guard clauses (plan §2/§9). Mirrors the
    // Core DTO so the landing page can render the same age-range +
    // allowed-gender chips without opening the edit form.
    int? MinAge = null,
    int? MaxAge = null,
    Guid? AllowedGenderCodedValueId = null);

public sealed record ActivityGroupDto(
    Guid Id,
    string Name,
    string? Description,
    string? Category,
    Guid? PeriodId,
    int? Capacity,
    string Status,
    int ActiveMemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record MembershipDto(
    Guid Id,
    Guid ActivityGroupId,
    Guid StudentId,
    string StudentName,
    DateOnly JoinedOn,
    DateOnly? ExitedOn,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SubjectDto(
    Guid Id,
    Guid CodedValueId,
    string Code,
    string Name,
    int DisplayOrder,
    bool IsOverridden,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PeriodDto(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    Guid? NextPeriodId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StudentEnrollmentDto(
    Guid Id,
    Guid StudentId,
    Guid PeriodId,
    Guid GradeLevelId,
    Guid? GradeStrandCodedValueId,
    DateOnly EnrolledOn,
    DateOnly? ExitDate,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TopicAssignmentDto(
    Guid Id,
    string Audience,
    Guid? GradeLevelId,
    Guid? ActivityGroupId,
    Guid TopicId,
    DateOnly StartDate,
    DateOnly? EndDate,
    Guid? TopicStrandId,
    Guid? TopicLessonId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StudentTopicAssignmentDto(
    Guid Id,
    Guid StudentId,
    Guid TopicId,
    Guid PeriodId,
    bool IsOverride,
    string SourceType,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── Request records ─────────────────────────────────────────────────────────

public record CreateStudentRequest(
    string StudentNumber,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId);

public record UpdateStudentRequest(
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId);

public record CreateGradeLevelRequest(
    Guid CodedValueId,
    int Level,
    string Name,
    int DisplayOrder,
    int? MinAge = null,
    int? MaxAge = null,
    Guid? AllowedGenderCodedValueId = null);

public record GetOrCreateGradeLevelRequest(
    Guid CodedValueId,
    int Level,
    string Name,
    int DisplayOrder,
    int? MinAge = null,
    int? MaxAge = null,
    Guid? AllowedGenderCodedValueId = null);

public record UpdateGradeLevelRequest(
    int Level,
    string Name,
    int DisplayOrder,
    int? MinAge = null,
    int? MaxAge = null,
    Guid? AllowedGenderCodedValueId = null);

public record CreateActivityGroupRequest(
    string Name,
    string? Description = null,
    string? Category = null,
    Guid? PeriodId = null,
    int? Capacity = null);

public record UpdateActivityGroupRequest(
    string Name,
    string? Description = null,
    string? Category = null,
    Guid? PeriodId = null,
    int? Capacity = null);

public record AddActivityGroupMemberRequest(
    Guid StudentId,
    DateOnly? JoinedOn = null);

public record CreateSubjectRequest(
    Guid CodedValueId,
    string Code,
    string Name,
    int DisplayOrder);

public record CreateSubjectForGradeRequest(
    Guid GradeLevelId,
    Guid? CodedValueId,
    string Code,
    string Name,
    int DisplayOrder);

public record UpdateSubjectRequest(
    string Name,
    int DisplayOrder);

public record CreatePeriodRequest(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate);

public record UpdatePeriodRequest(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate);

public record EnrollStudentRequest(
    Guid StudentId,
    Guid PeriodId,
    Guid GradeLevelId,
    Guid? GradeStrandCodedValueId,
    DateOnly? EnrolledOn);

public record TransferStudentRequest(
    Guid NewGradeLevelId,
    Guid? NewGradeStrandCodedValueId,
    DateOnly? TransferDate,
    string Reason);

public record WithdrawStudentRequest(
    DateOnly? ExitDate);

public record AssignGradeTopicRequest(
    Guid GradeLevelId,
    Guid TopicId,
    DateOnly StartDate,
    DateOnly? EndDate = null);

public record AssignStudentTopicRequest(
    Guid StudentId,
    Guid TopicId,
    Guid PeriodId,
    bool IsOverride,
    string SourceType);

// ── Guardian requests ────────────────────────────────────────────────────────

public record CreateGuardianRequest(
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    string? Address,
    Guid? CommunityId);

public record UpdateGuardianRequest(
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    string? Address,
    Guid? CommunityId);

public record LinkGuardianRequest(
    Guid StudentId,
    Guid GuardianId,
    Guid? RelationshipCodedValueId,
    GuardianRole Role,
    bool IsEmergencyContact,
    Guid? ActingGuardianId);

public record UpdateGuardianLinkRequest(
    GuardianRole Role,
    Guid? RelationshipCodedValueId,
    bool IsEmergencyContact);

// ── Teacher requests (Phase 8 / spec §4.12) ────────────────────────────────

public sealed record TeacherDto(
    Guid Id,
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    string Email,
    string? ContactPhone,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateTeacherRequest(
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    string Email,
    string? ContactPhone);

public record UpdateTeacherRequest(
    string FirstName,
    string LastName,
    string? DisplayName,
    string Email,
    string? ContactPhone);

public record LinkTeacherSubjectRequest(Guid SubjectId);

public record LinkTeacherGradeLevelRequest(Guid GradeLevelId);

// ── Client ──────────────────────────────────────────────────────────────────

public sealed class StudentsApiClient : IContactsClient
{
    private readonly HttpClient _http;
    private readonly ILogger<StudentsApiClient> _logger;
    private readonly CodedValuesApiClient _codedValues;

    public StudentsApiClient(HttpClient http, ILogger<StudentsApiClient> logger, CodedValuesApiClient codedValues)
    {
        _http = http;
        _logger = logger;
        _codedValues = codedValues;
    }

    // ── Students ─────────────────────────────────────────────────────────────

    public async Task<StudentDto[]?> ListStudentsAsync(CancellationToken ct = default, string? search = null)
    {
        var url = string.IsNullOrWhiteSpace(search) ? "/students" : $"/students?search={Uri.EscapeDataString(search)}";
        return await EnrichStudentsAsync(await _http.GetFromJsonAsync<StudentDto[]>(url, ct), ct);
    }

    public async Task<StudentDto[]?> ListDeletedStudentsAsync(CancellationToken ct = default) =>
        await EnrichStudentsAsync(await _http.GetFromJsonAsync<StudentDto[]>("/students/deleted", ct), ct);

    public async Task<StudentDto[]?> ListStudentsByGradeAsync(Guid gradeLevelId, Guid? periodId = null, CancellationToken ct = default)
    {
        var url = periodId.HasValue
            ? $"/students/by-grade/{gradeLevelId}?periodId={periodId}"
            : $"/students/by-grade/{gradeLevelId}";
        return await EnrichStudentsAsync(await _http.GetFromJsonAsync<StudentDto[]>(url, ct), ct);
    }

    public async Task<StudentDto?> GetStudentByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/students/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await EnrichSingleAsync(await response.Content.ReadFromJsonAsync<StudentDto>(ct), ct);
    }

    public async Task<StudentDto?> GetStudentByNumberAsync(string studentNumber, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/students/by-number/{Uri.EscapeDataString(studentNumber)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await EnrichSingleAsync(await response.Content.ReadFromJsonAsync<StudentDto>(ct), ct);
    }

    // ── DTO enrichment (client service) ──────────────────────────────────────
    // Age + GenderName are computed here (not in the UI, not in Students.Core)
    // so the Students module stays decoupled from the CodedValues module. The
    // server projection leaves them null; we fill them once, batched.
    private static int? ComputeAge(DateOnly? dob)
    {
        if (dob is not { } d) return null;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var age = today.Year - d.Year;
        if (d > today.AddYears(-age)) age--;
        return age;
    }

    private async Task<StudentDto[]?> EnrichStudentsAsync(StudentDto[]? items, CancellationToken ct = default)
    {
        if (items is null || items.Length == 0) return items;

        var withAge = items.Select(s => s with { Age = ComputeAge(s.DateOfBirth) }).ToArray();
        var genderIds = withAge.Select(s => s.GenderCodedValueId).OfType<Guid>().Distinct().ToArray();
        if (genderIds.Length == 0) return withAge;

        var names = await _codedValues.GetByIdsAsync(genderIds, ct);
        var map = names.ToDictionary(x => x.Id, x => x.Name);
        
        // Enrich with grade level info
        var studentIds = withAge.Select(s => s.Id).ToArray();
        var enrollmentsByStudent = new Dictionary<Guid, StudentEnrollmentDto[]>();
        
        // Get enrollments for each student
        foreach (var studentId in studentIds)
        {
            try
            {
                var enrollments = await ListEnrollmentsByStudentAsync(studentId, ct);
                if (enrollments != null)
                {
                    enrollmentsByStudent[studentId] = enrollments;
                }
            }
            catch (Exception)
            {
                // Continue with other students if one fails
            }
        }

        // Get all grade level IDs needed from enrollments
        var gradeIds = enrollmentsByStudent
            .SelectMany(kvp => kvp.Value)
            .Select(e => e.GradeLevelId)
            .Distinct()
            .ToArray();

        var gradeDict = new Dictionary<Guid, GradeLevelDto?>();
        if (gradeIds.Length > 0)
        {
            var grades = await ListGradeLevelsAsync(ct);
            if (grades != null)
            {
                gradeDict = grades.ToDictionary(g => g.Id, g => (GradeLevelDto?)g);
            }
        }

        return withAge.Select(s => s with
        {
            Age = ComputeAge(s.DateOfBirth),
            GenderName = s.GenderCodedValueId is { } id && map.TryGetValue(id, out var name) ? name : null,
            CurrentGrade = GetCurrentGrade(s.Id, enrollmentsByStudent, gradeDict)
        }).ToArray();
    }

    private static GradeLevelDto? GetCurrentGrade(Guid studentId,
        Dictionary<Guid, StudentEnrollmentDto[]> enrollmentsByStudent,
        Dictionary<Guid, GradeLevelDto?> gradeDict)
    {
        if (!enrollmentsByStudent.TryGetValue(studentId, out var enrollments) || enrollments.Length == 0)
            return null;

        // Get the most recent active enrollment
        var currentEnrollment = enrollments
            .Where(e => e.Status == "Active" || e.ExitDate == null)
            .OrderByDescending(e => e.EnrolledOn)
            .FirstOrDefault();
            
        if (currentEnrollment == null)
            return null;

        gradeDict.TryGetValue(currentEnrollment.GradeLevelId, out var grade);
        return grade;
    }

    private async Task<StudentDto?> EnrichSingleAsync(StudentDto? item, CancellationToken ct = default)
        => item is null ? null : (await EnrichStudentsAsync(new[] { item }, ct))?[0];

    public async Task<Guid> CreateStudentAsync(CreateStudentRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task UpdateStudentAsync(Guid id, UpdateStudentRequest req, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/students/{id}", req, ct)).EnsureSuccessStatusCode();

    public async Task DeleteStudentAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/students/{id}", ct)).EnsureSuccessStatusCode();

    public async Task RecoverStudentAsync(Guid id, CancellationToken ct = default) =>
        (await _http.PostAsync($"/students/{id}/recover", null, ct)).EnsureSuccessStatusCode();

    // ── Grade Levels ─────────────────────────────────────────────────────────

    public async Task<GradeLevelDto[]?> ListGradeLevelsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/students/grade-levels", ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"ListGradeLevels failed ({(int)response.StatusCode} {response.StatusCode}): {body}",
                inner: null,
                statusCode: response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<GradeLevelDto[]>(ct);
    }

    public async Task<GradeLevelLandingDto[]?> ListGradeLevelsForLandingAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<GradeLevelLandingDto[]>("/students/grade-levels/landing", ct);

    public async Task<GradeLevelDto?> GetGradeLevelByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/students/grade-levels/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GradeLevelDto>(ct);
    }

    public async Task<Guid> CreateGradeLevelAsync(CreateGradeLevelRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/grade-levels", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    /// <summary>
    /// Find-or-create a <see cref="GradeLevel"/> by <see cref="GetOrCreateGradeLevelRequest.CodedValueId"/>.
    /// Returns the resolved <see cref="GradeLevelDto"/> (existing or newly created).
    /// Used by the wizard save (§6.3).
    /// </summary>
    public async Task<GradeLevelDto> GetOrCreateGradeLevelAsync(GetOrCreateGradeLevelRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/grade-levels/get-or-create", req, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GradeLevelDto>(ct))!;
    }

    public async Task UpdateGradeLevelAsync(Guid id, UpdateGradeLevelRequest req, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/students/grade-levels/{id}", req, ct)).EnsureSuccessStatusCode();

    public async Task DeleteGradeLevelAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/students/grade-levels/{id}", ct)).EnsureSuccessStatusCode();

    // ── Activity groups (Phase 2 API, spec §7.1/§7.2) ────────────────────────

    public async Task<ActivityGroupDto[]?> ListActivityGroupsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<ActivityGroupDto[]>("/activity-groups", ct);

    public async Task<ActivityGroupDto?> GetActivityGroupByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/activity-groups/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ActivityGroupDto>(ct);
    }

    public async Task<Guid> CreateActivityGroupAsync(CreateActivityGroupRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/activity-groups", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task UpdateActivityGroupAsync(Guid id, UpdateActivityGroupRequest req, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/activity-groups/{id}", req, ct)).EnsureSuccessStatusCode();

    public async Task DeleteActivityGroupAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/activity-groups/{id}", ct)).EnsureSuccessStatusCode();

    public async Task ArchiveActivityGroupAsync(Guid id, CancellationToken ct = default) =>
        (await _http.PostAsync($"/activity-groups/{id}/archive", null, ct)).EnsureSuccessStatusCode();

    public async Task SuspendActivityGroupAsync(Guid id, CancellationToken ct = default) =>
        (await _http.PostAsync($"/activity-groups/{id}/suspend", null, ct)).EnsureSuccessStatusCode();

    public async Task<MembershipDto[]?> ListGroupMembersAsync(Guid groupId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<MembershipDto[]>($"/activity-groups/{groupId}/members", ct);

    public async Task AddGroupMemberAsync(Guid groupId, AddActivityGroupMemberRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"/activity-groups/{groupId}/members", req, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveGroupMemberAsync(Guid groupId, Guid studentId, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/activity-groups/{groupId}/members/{studentId}", ct)).EnsureSuccessStatusCode();

    /// <summary>
    /// Lists the activity groups a student is an active member of (spec §7.2,
    /// <c>GET /api/students/{studentId}/activity-groups</c>). Returns the
    /// groups the student currently belongs to, ordered by name.
    /// </summary>
    public async Task<ActivityGroupDto[]?> ListStudentGroupsAsync(Guid studentId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<ActivityGroupDto[]>($"/students/{studentId}/activity-groups", ct);

    // ── Topics ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists the topic (subject) catalog. Uses the canonical <c>/students/topics</c>
    /// route (NFR-6); the <c>/subjects</c> alias is deprecated. Returns Core
    /// <see cref="TopicDto"/> rows so the grade-level dialogs bind topic ids.
    /// </summary>
    public async Task<TopicDto[]?> ListTopicsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<TopicDto[]>("/students/topics", ct);

    public async Task<SubjectDto[]?> ListSubjectsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<SubjectDto[]>("/students/subjects", ct);

    public async Task<SubjectDto[]?> ListSubjectsByGradeAsync(Guid gradeLevelId, Guid? periodId = null, CancellationToken ct = default)
    {
        var url = periodId.HasValue
            ? $"/students/subjects/by-grade/{gradeLevelId}?periodId={periodId}"
            : $"/students/subjects/by-grade/{gradeLevelId}";
        return await _http.GetFromJsonAsync<SubjectDto[]>(url, ct);
    }

    public async Task<SubjectDto?> GetSubjectByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/students/subjects/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SubjectDto>(ct);
    }

    public async Task<Guid> CreateSubjectAsync(CreateSubjectRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/subjects", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    /// <summary>
    /// Find-or-create a <see cref="SubjectDto"/> by CodedValueId. Reuses the
    /// existing subject (updating mirrored fields) or creates a new one. Used
    /// by the wizard's "Add to grade" flow so the user can pick a subject
    /// coded value and wire it to the grade without leaving the wizard.
    /// </summary>
    public async Task<SubjectDto> GetOrCreateSubjectAsync(CreateSubjectRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/subjects/get-or-create", req, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SubjectDto>(ct))!;
    }

    /// <summary>
    /// Creates (or reuses) a <see cref="Subject"/> and a
    /// <see cref="GradeTopicAssignment"/> for the current period, linking the
    /// subject to the given grade level (§8.1). Returns the resolved SubjectDto.
    /// </summary>
    public async Task<SubjectDto> CreateSubjectForGradeAsync(CreateSubjectForGradeRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/subjects/for-grade", req, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SubjectDto>(ct))!;
    }

    public async Task UpdateSubjectAsync(Guid id, UpdateSubjectRequest req, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/students/subjects/{id}", req, ct)).EnsureSuccessStatusCode();

    public async Task DeleteSubjectAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/students/subjects/{id}", ct)).EnsureSuccessStatusCode();

    // ── Periods ──────────────────────────────────────────────────────────────

    public async Task<PeriodDto[]?> ListPeriodsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/students/periods", ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"ListPeriods failed ({(int)response.StatusCode} {response.StatusCode}): {body}",
                inner: null,
                statusCode: response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<PeriodDto[]>(ct);
    }

    public async Task<PeriodDto?> GetPeriodByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/students/periods/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PeriodDto>(ct);
    }

    public async Task<Guid> CreatePeriodAsync(CreatePeriodRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/periods", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task UpdatePeriodAsync(Guid id, UpdatePeriodRequest req, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/students/periods/{id}", req, ct)).EnsureSuccessStatusCode();

    public async Task ActivatePeriodAsync(Guid id, CancellationToken ct = default) =>
        (await _http.PostAsync($"/students/periods/{id}/activate", null, ct)).EnsureSuccessStatusCode();

    public async Task CompletePeriodAsync(Guid id, CancellationToken ct = default) =>
        (await _http.PostAsync($"/students/periods/{id}/complete", null, ct)).EnsureSuccessStatusCode();

    // ── Enrollments ───────────────────────────────────────────────────────────

    public async Task<StudentEnrollmentDto[]?> ListEnrollmentsByStudentAsync(Guid studentId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<StudentEnrollmentDto[]>($"/students/enrollments/by-student/{studentId}", ct);

    public async Task<StudentEnrollmentDto[]?> ListEnrollmentsByPeriodAsync(Guid periodId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<StudentEnrollmentDto[]>($"/students/enrollments/by-period/{periodId}", ct);

    public async Task<Guid> EnrollStudentAsync(EnrollStudentRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/enrollments", req, ct);
        // IMPORTANT: do NOT use EnsureSuccessStatusCode here. The
        // default HttpRequestException it throws only carries the
        // status code text ("Response status code does not indicate
        // success: 400 (Bad Request).") and DROPS the response body.
        // The server's body is where the actual tracing detail lives
        // (e.g. "Cannot enrol students: no active period is open
        // for this tenant. Open a period before enrolling." for
        // PeriodNotOpenException). Without the body, the dialog's
        // per-field error MessageBar shows just the generic status
        // text — useless for tracing WHAT went wrong. We therefore
        // check the status manually, read the body on failure, and
        // rethrow an HttpRequestException whose Message includes
        // BOTH the status code AND the body. The dialog's
        // `Error = ex.Message` then surfaces the full detail.
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"EnrollStudent failed ({(int)response.StatusCode} {response.StatusCode}): {body}",
                inner: null,
                statusCode: response.StatusCode);
        }
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task TransferStudentAsync(Guid enrollmentId, TransferStudentRequest req, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync($"/students/enrollments/{enrollmentId}/transfer", req, ct)).EnsureSuccessStatusCode();

    public async Task WithdrawStudentAsync(Guid enrollmentId, WithdrawStudentRequest req, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync($"/students/enrollments/{enrollmentId}/withdraw", req, ct)).EnsureSuccessStatusCode();

    // ── Grade Subject Assignments ─────────────────────────────────────────────

    public async Task<TopicAssignmentDto[]?> ListGradeTopicsByGradeAsync(Guid gradeLevelId, DateOnly? effectiveDate = null, CancellationToken ct = default)
    {
        var url = effectiveDate is { } e
            ? $"/students/topic-assignments/by-grade/{gradeLevelId}?effectiveDate={e:yyyy-MM-dd}"
            : $"/students/topic-assignments/by-grade/{gradeLevelId}";
        return await _http.GetFromJsonAsync<TopicAssignmentDto[]>(url, ct);
    }

    public async Task<Guid> AssignGradeTopicAsync(AssignGradeTopicRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/topic-assignments/grade", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task RemoveTopicAssignmentAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/students/topic-assignments/{id}", ct)).EnsureSuccessStatusCode();

    // ── Student Subject Assignments ──────────────────────────────────────────

    public async Task<StudentTopicAssignmentDto[]?> ListStudentTopicsByStudentAsync(Guid studentId, Guid periodId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<StudentTopicAssignmentDto[]>($"/students/student-topics/by-student/{studentId}/period/{periodId}", ct);

    public async Task<StudentTopicAssignmentDto[]?> ListStudentTopicsByPeriodAsync(Guid periodId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<StudentTopicAssignmentDto[]>($"/students/student-topics/by-period/{periodId}", ct);

    public async Task<Guid> AssignStudentTopicAsync(AssignStudentTopicRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/student-topics", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task RemoveStudentTopicAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/students/student-topics/{id}", ct)).EnsureSuccessStatusCode();

    // ── Guardians ───────────────────────────────────────────────────────────
    // Guardian ENTITY routes (CRUD + name-history + students-for-guardian) are
    // the root-level /guardians group (StudentEndpoints.cs:
    //   var guardiansGroup = app.MapGroup("/guardians"); guardiansGroup.MapGuardianRoutes();),
    // NOT a /students/guardians sub-resource — a guardian is an independent
    // entity shared across students. The earlier client wrongly prefixed these
    // with /students and the resulting 405 surfaced when adding a guardian on
    // student edit (CreateGuardianAsync POST /students/guardians → no such
    // route). The student↔guardian LINK routes below ARE nested at
    // /students/{studentId}/guardians (a relationship under the student).
    // Mirrors the /contacts convention (see the contacts comment below).

    public async Task<GuardianDto[]?> ListGuardiansAsync(CancellationToken ct = default, string? search = null, Guid? excludeStudentId = null)
    {
        // Build the query string from whichever optional filters are set.
        // excludeStudentId hides guardians already linked to that student
        // (server-side exclusion) so the picker cannot double-link.
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(search))
            parts.Add($"search={Uri.EscapeDataString(search)}");
        if (excludeStudentId is { } sid)
            parts.Add($"excludeStudentId={sid:D}");
        var url = parts.Count == 0 ? "/guardians" : $"/guardians?{string.Join('&', parts)}";
        return await _http.GetFromJsonAsync<GuardianDto[]>(url, ct);
    }

    public async Task<GuardianDto?> GetGuardianByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/guardians/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GuardianDto>(ct);
    }

    public async Task<Guid> CreateGuardianAsync(CreateGuardianRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/guardians", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task UpdateGuardianAsync(Guid id, UpdateGuardianRequest req, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/guardians/{id}", req, ct)).EnsureSuccessStatusCode();

    public async Task DeleteGuardianAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/guardians/{id}", ct)).EnsureSuccessStatusCode();

    public async Task<GuardianNameHistoryDto[]?> GetGuardianNameHistoryAsync(Guid id, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<GuardianNameHistoryDto[]>($"/guardians/{id}/name-history", ct);

    public async Task<StudentDto[]?> ListStudentsForGuardianAsync(Guid guardianId, CancellationToken ct = default) =>
        await EnrichStudentsAsync(await _http.GetFromJsonAsync<StudentDto[]>($"/guardians/{guardianId}/students", ct), ct);

    // ── Student ↔ Guardian links ─────────────────────────────────────────────

    public async Task<StudentGuardianViewDto[]?> ListGuardiansByStudentAsync(Guid studentId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<StudentGuardianViewDto[]>($"/students/{studentId}/guardians", ct);

    public async Task<Guid> LinkGuardianAsync(LinkGuardianRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"/students/{req.StudentId}/guardians", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task UpdateGuardianLinkAsync(Guid studentId, Guid guardianId, UpdateGuardianLinkRequest req, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/students/{studentId}/guardians/{guardianId}", req, ct)).EnsureSuccessStatusCode();

    public async Task UnlinkGuardianAsync(Guid studentId, Guid guardianId, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/students/{studentId}/guardians/{guardianId}", ct)).EnsureSuccessStatusCode();

    // ── Teachers (Phase 8 / spec §4.12) ───────────────────────────────────

    public async Task<TeacherDto[]?> ListTeachersAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<TeacherDto[]>("/teachers", ct);

    public async Task<TeacherDto?> GetTeacherByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/teachers/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TeacherDto>(ct);
    }

    public async Task<Guid> CreateTeacherAsync(CreateTeacherRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/teachers", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task UpdateTeacherAsync(Guid id, UpdateTeacherRequest req, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/teachers/{id}", req, ct)).EnsureSuccessStatusCode();

    public async Task DeleteTeacherAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/teachers/{id}", ct)).EnsureSuccessStatusCode();

    public async Task LinkTeacherSubjectAsync(Guid teacherId, Guid subjectId, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync($"/teachers/{teacherId}/subjects", new LinkTeacherSubjectRequest(subjectId), ct)).EnsureSuccessStatusCode();

    public async Task UnlinkTeacherSubjectAsync(Guid teacherId, Guid subjectId, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/teachers/{teacherId}/subjects/{subjectId}", ct)).EnsureSuccessStatusCode();

    public async Task LinkTeacherGradeLevelAsync(Guid teacherId, Guid gradeLevelId, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync($"/teachers/{teacherId}/grade-levels", new LinkTeacherGradeLevelRequest(gradeLevelId), ct)).EnsureSuccessStatusCode();

    public async Task UnlinkTeacherGradeLevelAsync(Guid teacherId, Guid gradeLevelId, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/teachers/{teacherId}/grade-levels/{gradeLevelId}", ct)).EnsureSuccessStatusCode();

    public async Task<SubjectDto[]?> ListSubjectsForTeacherAsync(Guid teacherId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<SubjectDto[]>($"/teachers/{teacherId}/subjects", ct);

    public async Task<GradeLevelDto[]?> ListGradeLevelsForTeacherAsync(Guid teacherId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<GradeLevelDto[]>($"/teachers/{teacherId}/grade-levels", ct);

    // ── Contacts ──────────────────────────────────────────────────────────────
    // The contacts API is registered as a sibling top-level group in
    // SchoolCollab.Students.Api/StudentEndpoints.cs:
    //
    //     var contactsGroup = app.MapGroup("/contacts");
    //     contactsGroup.MapContactRoutes();
    //     contactsGroup.MapSubscriptionRoutes();
    //
    // The previous client prefixed these paths with `/students` and the
    // resulting 404 surfaced in the <ContactsEditor> messagebar. Routes
    // are the root-level /contacts group, NOT a /students/contacts
    // sub-resource — contacts are a cross-cutting concern (they can
    // belong to a student OR a guardian), not a child of /students.

    public async Task<ContactDto[]?> ListContactsAsync(ContactOwnerType ownerType, Guid ownerId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<ContactDto[]>($"/contacts?ownerType={ownerType}&ownerId={ownerId}", ct);

    public async Task<Guid> AddContactAsync(AddContactRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/contacts", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task UpdateContactAsync(Guid id, UpdateContactRequest req, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/contacts/{id}", req, ct)).EnsureSuccessStatusCode();

    public async Task DeleteContactAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/contacts/{id}", ct)).EnsureSuccessStatusCode();

    public async Task VerifyContactAsync(Guid id, CancellationToken ct = default) =>
        (await _http.PostAsync($"/contacts/{id}/verify", null, ct)).EnsureSuccessStatusCode();

    public async Task SetContactOrderAsync(Guid id, int order, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync($"/contacts/{id}/order", new { Order = order }, ct)).EnsureSuccessStatusCode();

    public async Task ReorderContactsAsync(ContactOwnerType ownerType, Guid ownerId, IReadOnlyList<Guid> orderedIds, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync("/contacts/reorder",
            new { OwnerType = ownerType, OwnerId = ownerId, OrderedContactIds = orderedIds }, ct)).EnsureSuccessStatusCode();

    public async Task<SubscribedContactDto[]?> ListSubscribedContactsAsync(
        ContactOwnerType ownerType, Guid? ownerId = null, SubscriptionScope? scope = null, CancellationToken ct = default)
    {
        var url = scope.HasValue
            ? $"/contacts/subscribed?ownerType={ownerType}&scope={scope}{(ownerId.HasValue ? $"&ownerId={ownerId}" : "")}"
            : $"/contacts/subscribed?ownerType={ownerType}{(ownerId.HasValue ? $"&ownerId={ownerId}" : "")}";
        return await _http.GetFromJsonAsync<SubscribedContactDto[]>(url, ct);
    }

    // ── Subscriptions ────────────────────────────────────────────────────────

    public async Task SubscribeAsync(
        Guid contactId, SubscriptionScope scope = SubscriptionScope.AllAssignments, Guid? scopeRefId = null, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync($"/contacts/{contactId}/subscribe", new SubscriptionRequest(scope, scopeRefId), ct)).EnsureSuccessStatusCode();

    public async Task UnsubscribeAsync(
        Guid contactId, SubscriptionScope scope = SubscriptionScope.AllAssignments, Guid? scopeRefId = null, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync($"/contacts/{contactId}/unsubscribe", new SubscriptionRequest(scope, scopeRefId), ct)).EnsureSuccessStatusCode();

    // ── Helper ──────────────────────────────────────────────────────────────

    private sealed record IdResponse(Guid Id);
}