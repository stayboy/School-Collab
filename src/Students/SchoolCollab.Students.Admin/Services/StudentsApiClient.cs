using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace SchoolCollab.Students.Admin.Services;

// ── DTOs ────────────────────────────────────────────────────────────────────

public sealed record StudentDto(
    Guid Id,
    string StudentNumber,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    string ContactEmail,
    string? ContactPhone,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GradeLevelDto(
    Guid Id,
    Guid CodedValueId,
    int Level,
    string Name,
    int DisplayOrder,
    int SubjectCount,
    int StudentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GradeLevelLandingDto(
    Guid Id,
    Guid CodedValueId,
    int Level,
    string Name,
    int DisplayOrder,
    int SubjectCount,
    int StudentCount,
    Guid? CurrentPeriodId,
    string? CurrentPeriodName,
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
    bool AllowSubjectOverrides,
    Guid? NextPeriodId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StudentEnrollmentDto(
    Guid Id,
    Guid StudentId,
    Guid PeriodId,
    Guid GradeLevelId,
    DateOnly EnrolledOn,
    DateOnly? ExitDate,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GradeSubjectAssignmentDto(
    Guid Id,
    Guid GradeLevelId,
    Guid SubjectId,
    Guid PeriodId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StudentSubjectAssignmentDto(
    Guid Id,
    Guid StudentId,
    Guid SubjectId,
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
    Guid? GenderCodedValueId,
    string ContactEmail,
    string? ContactPhone);

public record UpdateStudentRequest(
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    string ContactEmail,
    string? ContactPhone);

public record CreateGradeLevelRequest(
    Guid CodedValueId,
    int Level,
    string Name,
    int DisplayOrder);

public record GetOrCreateGradeLevelRequest(
    Guid CodedValueId,
    int Level,
    string Name,
    int DisplayOrder);

public record UpdateGradeLevelRequest(
    int Level,
    string Name,
    int DisplayOrder);

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
    DateOnly EndDate,
    bool AllowSubjectOverrides);

public record UpdatePeriodRequest(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    bool AllowSubjectOverrides);

public record EnrollStudentRequest(
    Guid StudentId,
    Guid PeriodId,
    Guid GradeLevelId,
    DateOnly? EnrolledOn);

public record TransferStudentRequest(
    Guid NewGradeLevelId,
    DateOnly? TransferDate);

public record WithdrawStudentRequest(
    DateOnly? ExitDate);

public record AssignGradeSubjectRequest(
    Guid GradeLevelId,
    Guid SubjectId,
    Guid PeriodId);

public record AssignStudentSubjectRequest(
    Guid StudentId,
    Guid SubjectId,
    Guid PeriodId,
    bool IsOverride,
    string SourceType);

// ── Client ──────────────────────────────────────────────────────────────────

public sealed class StudentsApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<StudentsApiClient> _logger;

    public StudentsApiClient(HttpClient http, ILogger<StudentsApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    // ── Students ─────────────────────────────────────────────────────────────

    public async Task<StudentDto[]?> ListStudentsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<StudentDto[]>("/students", ct);

    public async Task<StudentDto[]?> ListDeletedStudentsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<StudentDto[]>("/students/deleted", ct);

    public async Task<StudentDto[]?> ListStudentsByGradeAsync(Guid gradeLevelId, Guid? periodId = null, CancellationToken ct = default)
    {
        var url = periodId.HasValue
            ? $"/students/by-grade/{gradeLevelId}?periodId={periodId}"
            : $"/students/by-grade/{gradeLevelId}";
        return await _http.GetFromJsonAsync<StudentDto[]>(url, ct);
    }

    public async Task<StudentDto?> GetStudentByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/students/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StudentDto>(ct);
    }

    public async Task<StudentDto?> GetStudentByNumberAsync(string studentNumber, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/students/by-number/{Uri.EscapeDataString(studentNumber)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StudentDto>(ct);
    }

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

    public async Task<GradeLevelDto[]?> ListGradeLevelsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<GradeLevelDto[]>("/students/grade-levels", ct);

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

    // ── Subjects ─────────────────────────────────────────────────────────────

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
    /// <see cref="GradeSubjectAssignment"/> for the current period, linking the
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

    public async Task<PeriodDto[]?> ListPeriodsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<PeriodDto[]>("/students/periods", ct);

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
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task TransferStudentAsync(Guid enrollmentId, TransferStudentRequest req, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync($"/students/enrollments/{enrollmentId}/transfer", req, ct)).EnsureSuccessStatusCode();

    public async Task WithdrawStudentAsync(Guid enrollmentId, WithdrawStudentRequest req, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync($"/students/enrollments/{enrollmentId}/withdraw", req, ct)).EnsureSuccessStatusCode();

    // ── Grade Subject Assignments ─────────────────────────────────────────────

    public async Task<GradeSubjectAssignmentDto[]?> ListGradeSubjectsByPeriodAsync(Guid periodId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<GradeSubjectAssignmentDto[]>($"/students/grade-subjects/by-period/{periodId}", ct);

    public async Task<GradeSubjectAssignmentDto[]?> ListGradeSubjectsByGradeAsync(Guid gradeLevelId, Guid periodId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<GradeSubjectAssignmentDto[]>($"/students/grade-subjects/by-grade/{gradeLevelId}/period/{periodId}", ct);

    public async Task<Guid> AssignGradeSubjectAsync(AssignGradeSubjectRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/grade-subjects", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task RemoveGradeSubjectAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/students/grade-subjects/{id}", ct)).EnsureSuccessStatusCode();

    // ── Student Subject Assignments ──────────────────────────────────────────

    public async Task<StudentSubjectAssignmentDto[]?> ListStudentSubjectsByStudentAsync(Guid studentId, Guid periodId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<StudentSubjectAssignmentDto[]>($"/students/student-subjects/by-student/{studentId}/period/{periodId}", ct);

    public async Task<StudentSubjectAssignmentDto[]?> ListStudentSubjectsByPeriodAsync(Guid periodId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<StudentSubjectAssignmentDto[]>($"/students/student-subjects/by-period/{periodId}", ct);

    public async Task<Guid> AssignStudentSubjectAsync(AssignStudentSubjectRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/student-subjects", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task RemoveStudentSubjectAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/students/student-subjects/{id}", ct)).EnsureSuccessStatusCode();

    // ── Helper ───────────────────────────────────────────────────────────────

    private sealed record IdResponse(Guid Id);
}