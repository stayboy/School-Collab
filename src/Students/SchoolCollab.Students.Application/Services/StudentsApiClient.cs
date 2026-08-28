using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Core.Contracts;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Application.Services;

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
    GradeLevelDto? CurrentGrade = null,
    // Stream name of the current enrollment (resolved client-side via the
    // CodedValues module). Null when the enrollment has no stream or resolution
    // fails; rendered as "Grade (Stream)" in the students landing grid.
    string? CurrentStream = null,
    // Guardian count, populated client-side from the bulk guardian-counts endpoint
    int? GuardianCount = null,
    // Title salutation (SALUTS parent), projected server-side.
    Guid? TitleCodedValueId = null,
    // Postgres xmin row version (IHasRowVersion). Echoed back from the server so the
    // all-inclusive edit can send it as ExpectedRowVersion for optimistic concurrency.
    uint RowVersion = 0);

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
    Guid? AllowedGenderCodedValueId = null,
    bool IsBlockedFromEnrollment = false);

public sealed record GradeLevelLandingDto(
    Guid Id,
    Guid CodedValueId,
    string Name,
    int TopicCount,
    int StrandCount,
    int LessonCount,
    int StudentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Enrollment validation guard clauses (plan §2/§9). Mirrors the
    // Core DTO so the landing page can render the same age-range +
    // allowed-gender chips without opening the edit form.
    int? MinAge = null,
    int? MaxAge = null,
    Guid? AllowedGenderCodedValueId = null,
    bool IsBlockedFromEnrollment = false);

public sealed record ActivityGroupDto(
    Guid Id,
    string Name,
    string? Description,
    string? Category,
    int? Capacity,
    bool IsActive,
    string Span,
    DateOnly? EnrollmentStartDate,
    DateOnly? EnrollmentEndDate,
    bool AutoRenewDefault,
    Guid[] EligibleGradeIds,
    int ActiveMemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record MembershipDto(
    Guid Id,
    Guid ActivityGroupId,
    Guid StudentId,
    string StudentName,
    Guid? PeriodId,
    bool AutoRenew,
    DateOnly? WindowStartDate,
    DateOnly? WindowEndDate,
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
    string PeriodType,
    Guid? ParentPeriodId,
    Guid? NextPeriodId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StudentEnrollmentDto(
    Guid Id,
    Guid StudentId,
    Guid PeriodId,
    Guid GradeLevelId,
    Guid? StreamCodedValueId,
    DateOnly EnrolledOn,
    DateOnly? ExitDate,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GuardianCountDto(Guid StudentId, int Count);

public sealed record StudentCountDto(Guid GuardianId, int Count);

public sealed record TopicAssignmentDto(
    Guid Id,
    string Audience,
    Guid? GradeLevelId,
    Guid? ActivityGroupId,
    Guid TopicId,
    DateOnly StartDate,
    DateOnly? EndDate,
    Guid? TopicStrandId,
    Guid? PeriodId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GradeTopicCurriculumDto(
    Guid TopicId,
    string Name,
    string? Code,
    int StrandCount,
    int LessonCount);

public sealed record StudentTopicAssignmentDto(
    Guid Id,
    Guid StudentId,
    Guid TopicId,
    Guid PeriodId,
    bool IsOverride,
    string SourceType,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TopicStrandDto(
    Guid Id,
    Guid TopicId,
    Guid? ParentStrandId,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool IsLesson,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TopicLessonDto(
    Guid Id,
    Guid TopicId,
    Guid? StrandId,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool IsOpenEnded,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── Request records ─────────────────────────────────────────────────────────

public record CreateStudentRequest(
    string StudentNumber,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    Guid? TitleCodedValueId = null);

/// <summary>Atomic create-student-with-linked-data request (Unit of Work).
/// Mirrors the server <c>CreateStudentWithLinkedData</c> command
/// (<c>POST /students/with-linked-data</c>). Enrollment is optional and included
/// in the same transaction when <see cref="EnrollmentGradeLevelId"/> is set.</summary>
public record CreateStudentWithLinkedDataRequest(
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    Guid? TitleCodedValueId = null,
    GuardianDraftRequest[]? Guardians = null,
    Guid? EnrollmentGradeLevelId = null,
    Guid? EnrollmentPeriodId = null,
    Guid? StreamCodedValueId = null,
    DateOnly? EnrolledOn = null,
    ContactDraftRequest[]? Contacts = null);

/// <summary>A guardian row for <see cref="CreateStudentWithLinkedDataRequest"/> —
/// either references an existing guardian or supplies new-guardian demographics.</summary>
public record GuardianDraftRequest(
    Guid? ExistingGuardianId,
    GuardianRole Role,
    Guid? RelationshipCodedValueId = null,
    bool IsEmergencyContact = false,
    Guid? ActingGuardianId = null,
    Guid? TitleCodedValueId = null,
    string? FirstName = null,
    string? LastName = null,
    Guid? GenderCodedValueId = null,
    DateOnly? DateOfBirth = null,
    ContactDraftRequest[]? Contacts = null);

/// <summary>A contact row for the create/update student requests (reserved shape).
/// <c>Id</c> is null for a new contact; set for an update (the all-inclusive edit
/// reconciles contact rows by id).</summary>
public record ContactDraftRequest(
    ContactChannel Channel,
    string Value,
    string? Label = null,
    string? CountryCode = null,
    int DisplayOrder = 0,
    Guid? Id = null);

public record UpdateStudentRequest(
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    Guid? TitleCodedValueId = null);

/// <summary>Atomic update-student-with-linked-data request (Unit of Work). Mirrors the
/// server <c>UpdateStudentWithLinkedData</c> command (<c>PUT /students/{id}/with-linked-data</c>).
/// <c>ExpectedRowVersion</c> is the student's <c>xmin</c> the client loaded (optimistic
/// concurrency); <c>LoadedGuardianIds</c>/<c>LoadedContactIds</c> are the guardian-link
/// guardian-ids / contact-ids the client saw at load, so the server can detect a
/// guardian link or contact row added or removed by another user since then.</summary>
public record UpdateStudentWithLinkedDataRequest(
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    Guid? GenderCodedValueId,
    Guid? TitleCodedValueId = null,
    uint ExpectedRowVersion = 0,
    GuardianDraftRequest[]? Guardians = null,
    ContactDraftRequest[]? Contacts = null,
    Guid[]? LoadedGuardianIds = null,
    Guid[]? LoadedContactIds = null);

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
    int? Capacity = null,
    string Span = "OpenEnded",
    DateOnly? EnrollmentStartDate = null,
    DateOnly? EnrollmentEndDate = null,
    bool AutoRenewDefault = true,
    Guid[]? EligibleGradeIds = null);

public record UpdateActivityGroupRequest(
    string Name,
    string? Description = null,
    string? Category = null,
    int? Capacity = null,
    DateOnly? EnrollmentStartDate = null,
    DateOnly? EnrollmentEndDate = null,
    bool? AutoRenewDefault = null,
    Guid[]? EligibleGradeIds = null);

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
    DateOnly EndDate,
    PeriodType PeriodType = PeriodType.AcademicYear,
    Guid? ParentPeriodId = null);

public record UpdatePeriodRequest(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    PeriodType PeriodType = PeriodType.AcademicYear,
    Guid? ParentPeriodId = null);

public record EnrollStudentRequest(
    Guid StudentId,
    Guid PeriodId,
    Guid GradeCodedValueId,
    Guid? StreamCodedValueId,
    DateOnly? EnrolledOn);

public record TransferStudentRequest(
    Guid NewGradeLevelId,
    Guid? NewStreamCodedValueId,
    DateOnly? TransferDate,
    string Reason);

public record WithdrawStudentRequest(
    DateOnly? ExitDate,
    string? Reason = null);

public record AssignGradeTopicRequest(
    Guid GradeLevelId,
    Guid TopicId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    Guid? PeriodId = null);

public record AssignActivityGroupTopicRequest(
    Guid ActivityGroupId,
    Guid TopicId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    Guid? PeriodId = null);

public record UpdateTopicAssignmentPeriodRequest(Guid? PeriodId);

public record AssignStudentTopicRequest(
    Guid StudentId,
    Guid TopicId,
    Guid PeriodId,
    bool IsOverride,
    string SourceType);

// ── Strand / Lesson requests ───────────────────────────────────────────────

public record CreateTopicStrandRequest(
    Guid TopicId,
    string Name,
    string? Description = null,
    int DisplayOrder = 0,
    Guid? ParentStrandId = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null);

public record UpdateTopicStrandRequest(
    string Name,
    string? Description = null,
    int DisplayOrder = 0,
    Guid? ParentStrandId = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null);

public record UpdateTopicRequest(
    string Name,
    int DisplayOrder = 0,
    Guid? CodedValueId = null,
    string? Code = null);

/// <summary>
/// Creates (or reuses) a shared Topic and links it to the given grade level for
/// the current period. Mirrors the server-side <c>CreateTopicForGrade</c> command
/// (<c>POST /students/topics/for-grade</c>). <paramref name="Code" /> is optional;
/// when omitted the server generates it from <paramref name="Name" />.
/// </summary>
public record CreateTopicForGradeRequest(
    Guid GradeLevelId,
    Guid? CodedValueId,
    string? Code,
    string Name,
    int DisplayOrder,
    Guid? PeriodId = null);

public record CreateTopicRequest(
    Guid CodedValueId,
    string Code,
    string Name,
    int DisplayOrder);

public record CreateTopicLessonRequest(
    Guid TopicId,
    string Name,
    string? Description = null,
    Guid? StrandId = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    bool IsOpenEnded = false,
    int DisplayOrder = 0);

public record UpdateTopicLessonRequest(
    string Name,
    string? Description = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    int DisplayOrder = 0);

public record AssignLessonStrandRequest(Guid? StrandId);

// ── Guardian requests ────────────────────────────────────────────────────────

public record CreateGuardianRequest(
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    string? Address,
    Guid? CommunityId,
    DateOnly? DateOfBirth = null,
    Guid? GenderCodedValueId = null);

public record UpdateGuardianRequest(
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    string? Address,
    Guid? CommunityId,
    DateOnly? DateOfBirth = null,
    Guid? GenderCodedValueId = null);

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
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? GenderCodedValueId = null,
    DateOnly? DateOfBirth = null,
    Guid? LevelOfEducationCodedValueId = null,
    Guid[]? QualificationCodedValueIds = null);

public record CreateTeacherRequest(
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    Guid? GenderCodedValueId = null,
    DateOnly? DateOfBirth = null,
    Guid? LevelOfEducationCodedValueId = null,
    Guid[]? QualificationCodedValueIds = null);

/// <summary>Atomic create-teacher-with-assignments request (Unit of Work).</summary>
public record CreateTeacherWithAssignmentsRequest(
    Guid? TitleCodedValueId,
    string FirstName,
    string LastName,
    string? DisplayName,
    Guid? GenderCodedValueId = null,
    DateOnly? DateOfBirth = null,
    Guid? LevelOfEducationCodedValueId = null,
    Guid[]? QualificationCodedValueIds = null,
    GradeAssignmentRequest[]? GradeAssignments = null,
    ActivityAssignmentRequest[]? ActivityAssignments = null);

/// <summary>A grade assignment row: grade + optional subject + optional role.</summary>
public record GradeAssignmentRequest(Guid GradeLevelId, Guid? SubjectId = null, Guid? RoleCodedValueId = null);

/// <summary>An activity assignment row: activity + optional role + optional grades.</summary>
public record ActivityAssignmentRequest(Guid ActivityGroupId, Guid? RoleCodedValueId = null, Guid[]? GradeLevelIds = null);

public record UpdateTeacherRequest(
    string FirstName,
    string LastName,
    string? DisplayName,
    Guid? GenderCodedValueId = null,
    DateOnly? DateOfBirth = null,
    Guid? LevelOfEducationCodedValueId = null,
    Guid[]? QualificationCodedValueIds = null);

public record LinkTeacherGradeLevelRequest(Guid GradeLevelId, Guid? TeacherRoleCodedValueId = null);

public record LinkTeacherGradeAssignmentRequest(Guid GradeLevelId, Guid? SubjectId = null, Guid? RoleCodedValueId = null);

public record LinkTeacherActivityAssignmentRequest(Guid ActivityGroupId, Guid? RoleCodedValueId = null, Guid[]? GradeLevelIds = null);

public record SetTeacherGradeLevelRoleRequest(Guid? TeacherRoleCodedValueId);

/// <summary>
/// A grade-scoped teaching assignment (v4 spec §3.5): grade + optional subject + role.
/// Returned by <c>ListTeacherGradeAssignmentsAsync</c> (GET /teachers/{id}/grade-assignments).
/// </summary>
public sealed record TeacherGradeAssignmentDto(
    Guid RowId,
    Guid GradeLevelId,
    string GradeName,
    int GradeLevel,
    Guid? SubjectId,
    string? SubjectName,
    string? SubjectCode,
    Guid? RoleCodedValueId);

/// <summary>
/// A teacher↔activity assignment (v4 spec §3.5): activity + role + optional grades.
/// Returned by <c>ListTeacherActivityAssignmentsAsync</c> (GET /teachers/{id}/activity-assignments).
/// </summary>
public sealed record TeacherActivityAssignmentDto(
    Guid RowId,
    Guid ActivityGroupId,
    string ActivityName,
    Guid? RoleCodedValueId,
    Guid[] GradeLevelIds);

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

        // Gender enrichment is optional — only when at least one student has a
        // gender coded value. It must NOT gate the grade enrichment below, or a
        // list of students with no gender would silently skip CurrentGrade.
        var genderIds = withAge.Select(s => s.GenderCodedValueId).OfType<Guid>().Distinct().ToArray();
        var map = new Dictionary<Guid, string>();
        if (genderIds.Length > 0)
        {
            var names = await _codedValues.GetByIdsAsync(genderIds, ct);
            map = names.ToDictionary(x => x.Id, x => x.Name);
        }

        // Enrich with grade level info (always runs, independent of gender)
        var studentIds = withAge.Select(s => s.Id).ToArray();
        var enrollmentsByStudent = new Dictionary<Guid, StudentEnrollmentDto[]>();

        // Bulk-load enrollments for ALL students in one round-trip (no N+1).
        // Any single enrollment fetch failing should not silently drop the rest
        // of the list — the per-student lookups used to be independently guarded,
        // so keep the same resilience at the bulk boundary: on failure we simply
        // leave enrollmentsByStudent empty (CurrentGrade renders null), matching
        // the old per-student catch-and-continue behavior.
        try
        {
            var allEnrollments = await ListEnrollmentsByStudentsAsync(studentIds, ct);
            enrollmentsByStudent = allEnrollments
                .GroupBy(e => e.StudentId)
                .ToDictionary(g => g.Key, g => g.ToArray());
        }
        catch (Exception)
        {
            // Continue with CurrentGrade unpopulated rather than failing the list.
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

        // Guardian counts: bulk-load the number of linked guardians per student
        // in one round-trip. On failure, leave GuardianCount null (column renders
        // "—") rather than killing the whole list — matching the resilience at the
        // other bulk boundaries above.
        var guardianCountByStudent = new Dictionary<Guid, int>();
        try
        {
            var counts = await ListGuardianCountsByStudentsAsync(studentIds, ct);
            guardianCountByStudent = counts.ToDictionary(c => c.StudentId, c => c.Count);
        }
        catch (Exception)
        {
            // Continue with GuardianCount unpopulated rather than failing the list.
        }

        // Stream names for current enrollments (optional enrichment — same
        // resilience as gender: a failed lookup must not gate CurrentGrade).
        var streamNames = new Dictionary<Guid, string>();
        try
        {
            var streamIds = enrollmentsByStudent.Values
                .Select(enrolls => GetCurrentEnrollment(enrolls))
                .OfType<StudentEnrollmentDto>()
                .Select(e => e.StreamCodedValueId)
                .OfType<Guid>()
                .Distinct()
                .ToArray();
            if (streamIds.Length > 0)
            {
                var streamDtos = await _codedValues.GetByIdsAsync(streamIds, ct);
                streamNames = streamDtos.ToDictionary(x => x.Id, x => x.Name);
            }
        }
        catch (Exception)
        {
            // Continue with CurrentStream unpopulated rather than failing the list.
        }

        return withAge.Select(s => s with
        {
            Age = ComputeAge(s.DateOfBirth),
            GenderName = s.GenderCodedValueId is { } id && map.TryGetValue(id, out var name) ? name : null,
            CurrentGrade = GetCurrentGrade(s.Id, enrollmentsByStudent, gradeDict),
            CurrentStream = GetCurrentStreamName(s.Id, enrollmentsByStudent, streamNames),
            GuardianCount = guardianCountByStudent.TryGetValue(s.Id, out var count) ? count : null
        }).ToArray();
    }

    /// <summary>
    /// The student's most recent active (or open-ended) enrollment — the single
    /// source for both <see cref="GetCurrentGrade"/> and <see cref="GetCurrentStreamName"/>.
    /// </summary>
    private static StudentEnrollmentDto? GetCurrentEnrollment(StudentEnrollmentDto[] enrollments) =>
        enrollments
            .Where(e => e.Status == "Active" || e.ExitDate == null)
            .OrderByDescending(e => e.EnrolledOn)
            .FirstOrDefault();
    private static GradeLevelDto? GetCurrentGrade(Guid studentId,
        Dictionary<Guid, StudentEnrollmentDto[]> enrollmentsByStudent,
        Dictionary<Guid, GradeLevelDto?> gradeDict)
    {
        if (!enrollmentsByStudent.TryGetValue(studentId, out var enrollments) || enrollments.Length == 0)
            return null;

        var currentEnrollment = GetCurrentEnrollment(enrollments);
        if (currentEnrollment == null)
            return null;

        gradeDict.TryGetValue(currentEnrollment.GradeLevelId, out var grade);
        return grade;
    }

    private static string? GetCurrentStreamName(Guid studentId,
        Dictionary<Guid, StudentEnrollmentDto[]> enrollmentsByStudent,
        Dictionary<Guid, string> streamNames)
    {
        if (!enrollmentsByStudent.TryGetValue(studentId, out var enrollments) || enrollments.Length == 0)
            return null;

        return GetCurrentEnrollment(enrollments)?.StreamCodedValueId is { } sid
            && streamNames.TryGetValue(sid, out var streamName)
            ? streamName
            : null;
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

    /// <summary>
    /// Atomically creates a student with its guardians, optional contacts, and an
    /// optional enrollment in one request. <c>POST /students/with-linked-data</c>.
    /// On failure the response body (with the server's domain-exception detail, e.g.
    /// "no active period is open") is surfaced so the dialog can render it.
    /// </summary>
    public async Task<Guid> CreateStudentWithLinkedDataAsync(
        CreateStudentWithLinkedDataRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/with-linked-data", req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"CreateStudentWithLinkedData failed ({(int)response.StatusCode} {response.StatusCode}): {body}",
                inner: null,
                statusCode: response.StatusCode);
        }
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task UpdateStudentAsync(Guid id, UpdateStudentRequest req, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/students/{id}", req, ct)).EnsureSuccessStatusCode();

    /// <summary>
    /// Atomically updates a student's profile + guardians + contact rows in one request
    /// (<c>PUT /students/{id}/with-linked-data</c>). On 409 (stale <c>ExpectedRowVersion</c>
    /// or a concurrent guardian/contact change) throws an <see cref="HttpRequestException"/>
    /// whose <see cref="HttpRequestException.StatusCode"/> is <see cref="HttpStatusCode.Conflict"/>
    /// so the edit dialog can surface a "reload and retry" message.
    /// </summary>
    public async Task UpdateStudentWithLinkedDataAsync(
        Guid id, UpdateStudentWithLinkedDataRequest req, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/students/{id}/with-linked-data", req, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"UpdateStudentWithLinkedData failed ({(int)response.StatusCode} {response.StatusCode}): {body}",
                inner: null,
                statusCode: response.StatusCode);
        }
    }

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

    // ── Per-grade notification policy override (null fields = inherit tenant default) ──

    public async Task<GradeNotificationPolicyDto?> GetGradeNotificationPolicyAsync(Guid gradeLevelId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/students/grade-levels/{gradeLevelId}/notification-policy", ct);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GradeNotificationPolicyDto>(ct);
    }

    public async Task UpsertGradeNotificationPolicyAsync(Guid gradeLevelId, UpsertGradeNotificationPolicyRequest req, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/students/grade-levels/{gradeLevelId}/notification-policy", req, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Blocks or unblocks a grade level from being used for student enrollment
    /// (the landing page's enrollment toggle). Throws on non-success (NotFound
    /// for a missing grade, Conflict on a concurrent write).
    /// </summary>
    public async Task SetGradeLevelEnrollmentBlockedAsync(Guid id, bool blocked, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync(
            $"/students/grade-levels/{id}/enrollment-blocked", new { Blocked = blocked }, ct);
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"SetGradeLevelEnrollmentBlocked failed ({(int)response.StatusCode} {response.StatusCode}): {body}",
            inner: null,
            statusCode: response.StatusCode);
    }

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

    public async Task ActivateActivityGroupAsync(Guid id, CancellationToken ct = default) =>
        (await _http.PostAsync($"/activity-groups/{id}/activate", null, ct)).EnsureSuccessStatusCode();

    public async Task DeactivateActivityGroupAsync(Guid id, CancellationToken ct = default) =>
        (await _http.PostAsync($"/activity-groups/{id}/deactivate", null, ct)).EnsureSuccessStatusCode();

    public async Task RolloverActivityGroupAsync(Guid id, CancellationToken ct = default) =>
        (await _http.PostAsync($"/activity-groups/{id}/rollover", null, ct)).EnsureSuccessStatusCode();

    /// <summary>
    /// Sets the next DateRange window in advance (spec FR-51/53). The backend
    /// rejects a next start before the current window's end.
    /// </summary>
    public async Task SetActivityGroupNextWindowAsync(Guid id, DateOnly nextStartDate, DateOnly nextEndDate, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/activity-groups/{id}/next-window", new { NextStartDate = nextStartDate, NextEndDate = nextEndDate }, ct)).EnsureSuccessStatusCode();

    public async Task<MembershipDto[]?> ListGroupMembersAsync(Guid groupId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<MembershipDto[]>($"/activity-groups/{groupId}/members", ct);

    public async Task AddGroupMemberAsync(Guid groupId, AddActivityGroupMemberRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"/activity-groups/{groupId}/members", req, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveGroupMemberAsync(Guid groupId, Guid studentId, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/activity-groups/{groupId}/members/{studentId}", ct)).EnsureSuccessStatusCode();

    public async Task ExitGroupMemberAsync(Guid groupId, Guid studentId, CancellationToken ct = default) =>
        (await _http.PostAsync($"/activity-groups/{groupId}/members/{studentId}/exit", null, ct)).EnsureSuccessStatusCode();

    public async Task SetMembershipAutoRenewAsync(Guid membershipId, bool autoRenew, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/activity-groups/members/{membershipId}/auto-renew", new { autoRenew }, ct)).EnsureSuccessStatusCode();

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

    /// <summary>
    /// Gets a topic by its id. <c>GET /students/topics/{id}</c>.
    /// Returns <see langword="null"/> when the topic is not found.
    /// </summary>
    public async Task<TopicDto?> GetTopicByIdAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/students/topics/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TopicDto>(ct);
    }

    /// <summary>Updates a topic's name / display order. <c>PUT /students/topics/{id}</c>.</summary>
    public async Task UpdateTopicAsync(Guid id, UpdateTopicRequest req, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/students/topics/{id}", req, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Creates (or reuses) a shared Topic and wires it to the given grade level
    /// in one atomic call. <c>POST /students/topics/for-grade</c>. Returns the
    /// resolved <see cref="TopicDto"/>. Used by the grade-detail Subjects card's
    /// Add affordance so a new subject (topic) can be created and assigned to the
    /// grade without leaving the page.
    /// </summary>
    public async Task<TopicDto> CreateTopicForGradeAsync(CreateTopicForGradeRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/topics/for-grade", req, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TopicDto>(ct))!;
    }

    public async Task<Guid> CreateTopicAsync(CreateTopicRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/topics", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task<SubjectDto[]?> ListSubjectsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<SubjectDto[]>("/students/subjects", ct);

    public async Task<SubjectDto[]?> ListSubjectsByGradeAsync(Guid gradeLevelId, Guid? periodId = null, CancellationToken ct = default)
    {
        var url = periodId.HasValue
            ? $"/students/subjects/by-grade/{gradeLevelId}?periodId={periodId}"
            : $"/students/subjects/by-grade/{gradeLevelId}";
        return await _http.GetFromJsonAsync<SubjectDto[]>(url, ct);
    }

    /// <summary>
    /// Lists topics assigned to a grade that are effective on the given date
    /// (spec FR-58). The backend filters by the topic assignment's effective
    /// <c>[StartDate, EndDate]</c> window and Rev. 6 <c>PeriodId</c>.
    /// </summary>
    public async Task<SubjectDto[]?> ListSubjectsByGradeEffectiveAsync(Guid gradeLevelId, DateOnly? effectiveDate, CancellationToken ct = default)
    {
        var url = effectiveDate.HasValue
            ? $"/students/subjects/by-grade/{gradeLevelId}?effectiveDate={effectiveDate:yyyy-MM-dd}"
            : $"/students/subjects/by-grade/{gradeLevelId}";
        return await _http.GetFromJsonAsync<SubjectDto[]>(url, ct);
    }

    /// <summary>
    /// Lists topics assigned to an activity group that are effective on the
    /// given date (spec FR-58). The backend filters by the topic assignment's
    /// effective <c>[StartDate, EndDate]</c> window and Rev. 6 <c>PeriodId</c>.
    /// </summary>
    public async Task<SubjectDto[]?> ListSubjectsByGroupAsync(Guid activityGroupId, DateOnly? effectiveDate, CancellationToken ct = default)
    {
        var url = effectiveDate.HasValue
            ? $"/students/subjects/by-group/{activityGroupId}?effectiveDate={effectiveDate:yyyy-MM-dd}"
            : $"/students/subjects/by-group/{activityGroupId}";
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

    // ── Strands ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists strands for a topic, optionally filtered to a parent's children
    /// (lessons under a strand). <c>GET /students/topics/{topicId}/strands</c>.
    /// </summary>
    public async Task<TopicStrandDto[]?> ListTopicStrandsAsync(Guid topicId, Guid? parentStrandId = null, CancellationToken ct = default) =>
        parentStrandId is { } pid
            ? await _http.GetFromJsonAsync<TopicStrandDto[]>($"/students/topics/{topicId}/strands?parentStrandId={pid}", ct)
            : await _http.GetFromJsonAsync<TopicStrandDto[]>($"/students/topics/{topicId}/strands", ct);

    /// <summary>
    /// Creates a new strand under a topic. <c>POST /students/topics/strands</c>.
    /// Returns the created <see cref="TopicStrandDto"/>.
    /// </summary>
    public async Task<TopicStrandDto> CreateTopicStrandAsync(CreateTopicStrandRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/topics/strands", req, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TopicStrandDto>(ct))!;
    }

    /// <summary>
    /// Updates an existing strand. <c>PUT /students/topics/strands/{id}</c>.
    /// Returns the updated <see cref="TopicStrandDto"/>.
    /// </summary>
    public async Task<TopicStrandDto> UpdateTopicStrandAsync(Guid id, UpdateTopicStrandRequest req, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/students/topics/strands/{id}", req, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TopicStrandDto>(ct))!;
    }

    /// <summary>
    /// Deletes a strand. <c>DELETE /students/topics/strands/{id}</c>.
    /// </summary>
    public async Task DeleteTopicStrandAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/students/topics/strands/{id}", ct)).EnsureSuccessStatusCode();

    // ── Lessons ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Lists all lessons for a topic, optionally filtered by strand.
    /// <c>GET /students/topics/{topicId}/lessons[?strandId=…]</c>.
    /// </summary>
    public async Task<TopicLessonDto[]?> ListTopicLessonsAsync(Guid topicId, Guid? strandId = null, CancellationToken ct = default)
    {
        var url = strandId.HasValue
            ? $"/students/topics/{topicId}/lessons?strandId={strandId}"
            : $"/students/topics/{topicId}/lessons";
        return await _http.GetFromJsonAsync<TopicLessonDto[]>(url, ct);
    }

    /// <summary>
    /// Creates a new lesson under a topic. <c>POST /students/topics/lessons</c>.
    /// Returns the created <see cref="TopicLessonDto"/>.
    /// </summary>
    public async Task<TopicLessonDto> CreateTopicLessonAsync(CreateTopicLessonRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/topics/lessons", req, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TopicLessonDto>(ct))!;
    }

    /// <summary>
    /// Updates an existing lesson. <c>PUT /students/topics/lessons/{id}</c>.
    /// Returns the updated <see cref="TopicLessonDto"/>.
    /// </summary>
    public async Task<TopicLessonDto> UpdateTopicLessonAsync(Guid id, UpdateTopicLessonRequest req, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/students/topics/lessons/{id}", req, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TopicLessonDto>(ct))!;
    }

    /// <summary>
    /// Assigns (or clears) the strand for a lesson.
    /// <c>POST /students/topics/lessons/{id}/strand</c>.
    /// Returns the updated <see cref="TopicLessonDto"/>.
    /// </summary>
    public async Task<TopicLessonDto> AssignLessonStrandAsync(Guid id, AssignLessonStrandRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"/students/topics/lessons/{id}/strand", req, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TopicLessonDto>(ct))!;
    }

    /// <summary>
    /// Deletes a lesson. <c>DELETE /students/topics/lessons/{id}</c>.
    /// </summary>
    public async Task DeleteTopicLessonAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/students/topics/lessons/{id}", ct)).EnsureSuccessStatusCode();

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

    public async Task<PeriodDto[]?> ListSubPeriodsAsync(Guid academicYearId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/students/periods/{academicYearId}/sub-periods", ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"ListSubPeriods failed ({(int)response.StatusCode} {response.StatusCode}): {body}",
                inner: null,
                statusCode: response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<PeriodDto[]>(ct);
    }

    public async Task<PeriodDto?> GetActiveAcademicYearAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/students/periods/active-academic-year", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PeriodDto>(ct);
    }

    public async Task<PeriodDto?> GetActiveSubPeriodAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/students/periods/active-sub-period", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PeriodDto>(ct);
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

    /// <summary>
    /// Bulk variant of <see cref="ListEnrollmentsByStudentAsync"/> — returns all
    /// non-deleted enrollments for many students in one round-trip. Used by the
    /// client-side enrichment (<c>EnrichStudentsAsync</c>) to hydrate CurrentGrade
    /// without an N+1 per-student fetch. Empty input → empty result (no request).
    /// </summary>
    public async Task<StudentEnrollmentDto[]> ListEnrollmentsByStudentsAsync(IEnumerable<Guid> studentIds, CancellationToken ct = default)
    {
        var ids = studentIds as Guid[] ?? studentIds.ToArray();
        if (ids.Length == 0) return [];
        var query = string.Join("&", ids.Select(id => $"studentIds={id}"));
        return await _http.GetFromJsonAsync<StudentEnrollmentDto[]>($"/students/enrollments/by-students?{query}", ct)
               ?? [];
    }

    /// <summary>
    /// Bulk guardian-count variant — returns the number of linked (non-deleted)
    /// guardians for each requested student in one round-trip. Used by
    /// <c>EnrichStudentsAsync</c> to hydrate the landing page's "N guardians"
    /// column without an N+1 per-student fetch. Empty input → empty result.
    /// </summary>
    public async Task<GuardianCountDto[]> ListGuardianCountsByStudentsAsync(IEnumerable<Guid> studentIds, CancellationToken ct = default)
    {
        var ids = studentIds as Guid[] ?? studentIds.ToArray();
        if (ids.Length == 0) return [];
        var query = string.Join("&", ids.Select(id => $"studentIds={id}"));
        return await _http.GetFromJsonAsync<GuardianCountDto[]>($"/students/guardian-counts?{query}", ct)
               ?? [];
    }

    /// <summary>
    /// Bulk student-count variant — returns the number of linked (non-deleted)
    /// students for each requested guardian in one round-trip. Used by
    /// <c>EnrichGuardiansAsync</c> to hydrate the guardians landing page's
    /// "N students" column without an N+1 per-guardian fetch. Empty input →
    /// empty result.
    /// </summary>
    public async Task<StudentCountDto[]> ListStudentCountsByGuardiansAsync(IEnumerable<Guid> guardianIds, CancellationToken ct = default)
    {
        var ids = guardianIds as Guid[] ?? guardianIds.ToArray();
        if (ids.Length == 0) return [];
        var query = string.Join("&", ids.Select(id => $"guardianIds={id}"));
        return await _http.GetFromJsonAsync<StudentCountDto[]>($"/guardians/student-counts?{query}", ct)
               ?? [];
    }

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

    /// <summary>
    /// Per-topic strand/lesson counts for a grade's assigned topics
    /// (grade-detail-rich-grids-plan.md §4).
    /// </summary>
    public async Task<GradeTopicCurriculumDto[]?> ListGradeTopicCurriculumByGradeAsync(Guid gradeLevelId, DateOnly? effectiveDate = null, CancellationToken ct = default)
    {
        var url = effectiveDate is { } e
            ? $"/students/grade-levels/{gradeLevelId}/curriculum?effectiveDate={e:yyyy-MM-dd}"
            : $"/students/grade-levels/{gradeLevelId}/curriculum";
        return await _http.GetFromJsonAsync<GradeTopicCurriculumDto[]>(url, ct);
    }

    public async Task<Guid> AssignGradeTopicAsync(AssignGradeTopicRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/topic-assignments/grade", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task<Guid> AssignActivityGroupTopicAsync(AssignActivityGroupTopicRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/students/topic-assignments/activity-group", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task RemoveTopicAssignmentAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/students/topic-assignments/{id}", ct)).EnsureSuccessStatusCode();

    public async Task UpdateTopicAssignmentPeriodAsync(Guid id, Guid? periodId, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/students/topic-assignments/{id}/period",
            new UpdateTopicAssignmentPeriodRequest(periodId), ct)).EnsureSuccessStatusCode();

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

    public async Task<GuardianDto[]?> ListGuardiansAsync(CancellationToken ct = default, string? search = null, Guid? excludeStudentId = null, Guid? studentId = null)
    {
        // Build the query string from whichever optional filters are set.
        // excludeStudentId hides guardians already linked to that student
        // (server-side exclusion) so the picker cannot double-link.
        // studentId restricts to guardians linked to that student (the
        // student-scoped guardians view reached from the landing grid).
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(search))
            parts.Add($"search={Uri.EscapeDataString(search)}");
        if (excludeStudentId is { } sid)
            parts.Add($"excludeStudentId={sid:D}");
        if (studentId is { } stid)
            parts.Add($"studentId={stid:D}");
        var url = parts.Count == 0 ? "/guardians" : $"/guardians?{string.Join('&', parts)}";
        var items = await _http.GetFromJsonAsync<GuardianDto[]>(url, ct);
        return await EnrichGuardiansAsync(items, ct);
    }

    /// <summary>
    /// Hydrates the guardians list's "N students" column in one bulk round-trip
    /// (no N+1). On failure, leaves <see cref="GuardianDto.StudentCount"/> null
    /// (column renders "—") rather than failing the whole list — matching the
    /// resilience at the student enrichment boundaries.
    /// </summary>
    private async Task<GuardianDto[]?> EnrichGuardiansAsync(GuardianDto[]? items, CancellationToken ct = default)
    {
        if (items is null || items.Length == 0) return items;

        var studentCountByGuardian = new Dictionary<Guid, int>();
        try
        {
            var counts = await ListStudentCountsByGuardiansAsync(items.Select(g => g.Id), ct);
            studentCountByGuardian = counts.ToDictionary(c => c.GuardianId, c => c.Count);
        }
        catch (Exception)
        {
            // Continue with StudentCount unpopulated rather than failing the list.
        }

        // Resolve salutation titles in one bulk call so the landing page can
        // render the same combined "title + name" format as GuardianGrid. On
        // failure, leave TitleName null (the Name cell falls back to
        // DisplayName-or-FirstLast) rather than failing the whole list.
        var titleIds = items.Select(g => g.TitleCodedValueId).OfType<Guid>().Distinct().ToArray();
        var titleByCodedValue = new Dictionary<Guid, string>();
        if (titleIds.Length > 0)
        {
            try
            {
                var titles = await _codedValues.GetByIdsAsync(titleIds, ct);
                titleByCodedValue = titles.ToDictionary(t => t.Id, t => t.Name);
            }
            catch (Exception)
            {
                // Continue with TitleName unpopulated rather than failing the list.
            }
        }

        // Resolve relationship coded values (only present when the list is scoped
        // to one student via ?studentId=) in one bulk call so the student-scoped
        // landing page can render "name (relationship)". On failure, leave
        // RelationshipName null rather than failing the whole list.
        var relationshipIds = items.Select(g => g.RelationshipCodedValueId).OfType<Guid>().Distinct().ToArray();
        var relationshipByCodedValue = new Dictionary<Guid, string>();
        if (relationshipIds.Length > 0)
        {
            try
            {
                var rels = await _codedValues.GetByIdsAsync(relationshipIds, ct);
                relationshipByCodedValue = rels.ToDictionary(r => r.Id, r => r.Name);
            }
            catch (Exception)
            {
                // Continue with RelationshipName unpopulated rather than failing the list.
            }
        }

        return items.Select(g => g with
        {
            StudentCount = studentCountByGuardian.TryGetValue(g.Id, out var count) ? count : null,
            TitleName = g.TitleCodedValueId is { } tid && titleByCodedValue.TryGetValue(tid, out var title) ? title : null,
            RelationshipName = g.RelationshipCodedValueId is { } rid && relationshipByCodedValue.TryGetValue(rid, out var rel) ? rel : null
        }).ToArray();
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

    /// <summary>
    /// Atomically creates a teacher with its grade and activity assignments in a
    /// single transaction. If any assignment fails, the whole create is rolled back.
    /// </summary>
    public async Task<Guid> CreateTeacherWithAssignmentsAsync(CreateTeacherWithAssignmentsRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/teachers/with-assignments", req, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IdResponse>(ct);
        return result!.Id;
    }

    public async Task UpdateTeacherAsync(Guid id, UpdateTeacherRequest req, CancellationToken ct = default) =>
        (await _http.PutAsJsonAsync($"/teachers/{id}", req, ct)).EnsureSuccessStatusCode();

    public async Task DeleteTeacherAsync(Guid id, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/teachers/{id}", ct)).EnsureSuccessStatusCode();

    public async Task LinkTeacherGradeLevelAsync(Guid teacherId, Guid gradeLevelId, Guid? roleCodedValueId = null, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync($"/teachers/{teacherId}/grade-levels", new LinkTeacherGradeLevelRequest(gradeLevelId, roleCodedValueId), ct)).EnsureSuccessStatusCode();

    public async Task UnlinkTeacherGradeLevelAsync(Guid teacherId, Guid gradeLevelId, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/teachers/{teacherId}/grade-levels/{gradeLevelId}", ct)).EnsureSuccessStatusCode();

    // Set/clear the coded-value role a teacher holds on a grade level
    // (grade-level-detail-view-plan.md §3.1). PATCH /teachers/{id}/grade-levels/{gradeId}/role.
    public async Task SetTeacherGradeLevelRoleAsync(Guid teacherId, Guid gradeLevelId, Guid? roleCodedValueId, CancellationToken ct = default) =>
        (await _http.PatchAsJsonAsync($"/teachers/{teacherId}/grade-levels/{gradeLevelId}/role", new SetTeacherGradeLevelRoleRequest(roleCodedValueId), ct)).EnsureSuccessStatusCode();

    // Teachers linked to a grade level with their role (grade-level-detail-view-plan.md §3.2).
    public async Task<TeacherWithRoleDto[]?> ListTeachersForGradeLevelAsync(Guid gradeLevelId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<TeacherWithRoleDto[]>($"/students/grade-levels/{gradeLevelId}/teachers", ct);

    public async Task<GradeLevelDto[]?> ListGradeLevelsForTeacherAsync(Guid teacherId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<GradeLevelDto[]>($"/teachers/{teacherId}/grade-levels", ct);

    // ── v4 assignments (grade + optional subject + role; activity + role + grades) ──

    public async Task<TeacherGradeAssignmentDto[]?> ListTeacherGradeAssignmentsAsync(Guid teacherId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<TeacherGradeAssignmentDto[]>($"/teachers/{teacherId}/grade-assignments", ct);

    public async Task LinkTeacherGradeAssignmentAsync(Guid teacherId, Guid gradeLevelId, Guid? subjectId = null, Guid? roleCodedValueId = null, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync($"/teachers/{teacherId}/grade-assignments", new LinkTeacherGradeAssignmentRequest(gradeLevelId, subjectId, roleCodedValueId), ct)).EnsureSuccessStatusCode();

    public async Task DeleteTeacherGradeAssignmentAsync(Guid teacherId, Guid rowId, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/teachers/{teacherId}/grade-assignments/{rowId}", ct)).EnsureSuccessStatusCode();

    public async Task<TeacherActivityAssignmentDto[]?> ListTeacherActivityAssignmentsAsync(Guid teacherId, CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<TeacherActivityAssignmentDto[]>($"/teachers/{teacherId}/activity-assignments", ct);

    public async Task LinkTeacherActivityAssignmentAsync(Guid teacherId, Guid activityGroupId, Guid? roleCodedValueId = null, Guid[]? gradeLevelIds = null, CancellationToken ct = default) =>
        (await _http.PostAsJsonAsync($"/teachers/{teacherId}/activity-assignments", new LinkTeacherActivityAssignmentRequest(activityGroupId, roleCodedValueId, gradeLevelIds), ct)).EnsureSuccessStatusCode();

    public async Task DeleteTeacherActivityAssignmentAsync(Guid teacherId, Guid rowId, CancellationToken ct = default) =>
        (await _http.DeleteAsync($"/teachers/{teacherId}/activity-assignments/{rowId}", ct)).EnsureSuccessStatusCode();

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

    public async Task<ContactAuditEntryDto[]?> ListContactAuditEntriesAsync(
        Guid? contactId = null,
        ContactOwnerType? ownerType = null,
        Guid? ownerId = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        var query = new List<string>();
        if (contactId.HasValue) query.Add($"contactId={contactId.Value:D}");
        if (ownerType.HasValue) query.Add($"ownerType={ownerType.Value}");
        if (ownerId.HasValue) query.Add($"ownerId={ownerId.Value:D}");
        query.Add($"skip={skip}");
        query.Add($"take={take}");
        var url = "/contacts/audit?" + string.Join("&", query);
        return await _http.GetFromJsonAsync<ContactAuditEntryDto[]>(url, ct);
    }

    public async Task UpdateContactAsync(Guid id, UpdateContactRequest req, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/contacts/{id}", req, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteContactAsync(Guid id, string reason, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(reason)
            ? $"/contacts/{id}"
            : $"/contacts/{id}?reason={Uri.EscapeDataString(reason)}";
        (await _http.DeleteAsync(url, ct)).EnsureSuccessStatusCode();
    }

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

/// <summary>Upsert request for a per-grade notification policy override (all-nullable:
/// a null field clears to "inherit the tenant default"). Mirrors the Students API shape.</summary>
public sealed record UpsertGradeNotificationPolicyRequest(
    NotificationChannel[]? PreferredChannelOrder,
    NotificationChannel[]? BlockedChannels,
    int? MaxNotifications,
    int? MaxReminders,
    int? ReminderIntervalHours,
    int? LinkValidityDays,
    TimeOnly? SendoutTimeOfDay,
    int? SendoutIntervalMinutes);