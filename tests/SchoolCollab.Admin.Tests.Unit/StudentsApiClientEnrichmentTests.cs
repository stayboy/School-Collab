using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Services;
using GuardianDto = SchoolCollab.Students.Core.DTOs.GuardianDto;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Regression tests for the client-side enrichment in <see cref="StudentsApiClient"/>.
///
/// The Current Grade column on the student landing page is populated client-side
/// (server projection leaves <c>CurrentGrade</c> null). This enrichment used to
/// early-return when NO student had a gender coded value, which silently skipped
/// grade lookup — the Current Grade column rendered "—" for every student in a
/// gender-less list. These tests pin the enrichment so CurrentGrade is always
/// computed, independent of whether gender enrichment runs.
/// </summary>
[TestClass]
public class StudentsApiClientEnrichmentTests
{
    // Matches on the absolute path only (query string ignored), so a single
    // handler can serve the students, enrollment, grade-levels, and coded-values
    // endpoints without prefix-matching collisions.
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> CallCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ScriptedHandler Map(string path, string body)
        {
            _responses[path] = body;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            CallCounts[path] = CallCounts.GetValueOrDefault(path) + 1;
            if (_responses.TryGetValue(path, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected path: {path}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    private static StudentsApiClient CreateClient(ScriptedHandler studentsHandler, ScriptedHandler? codedValuesHandler = null)
    {
        var http = new HttpClient(studentsHandler) { BaseAddress = new Uri("http://localhost") };
        var codedValues = new CodedValuesApiClient(new HttpClient(codedValuesHandler ?? new ScriptedHandler()) { BaseAddress = new Uri("http://localhost") });
        return new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues);
    }

    [TestMethod]
    public async Task ListStudents_PopulatesCurrentGrade_WhenStudentHasNoGender()
    {
        var studentId = Guid.NewGuid();
        var gradeId = Guid.NewGuid();

        var student = new StudentDto(
            Id: studentId, StudentNumber: "1001", FirstName: "Jane", LastName: "Doe",
            DateOfBirth: new DateOnly(2015, 1, 1), GenderCodedValueId: null, IsDeleted: false,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);
        var enrollment = new StudentEnrollmentDto(
            Id: Guid.NewGuid(), StudentId: studentId, PeriodId: Guid.NewGuid(), GradeLevelId: gradeId,
            GradeStrandCodedValueId: null, EnrolledOn: new DateOnly(2024, 9, 1), ExitDate: null,
            Status: "Active", CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);
        var grade = new GradeLevelDto(
            Id: gradeId, CodedValueId: Guid.NewGuid(), Level: 5, Name: "Grade 5", DisplayOrder: 1,
            TopicCount: 0, StudentCount: 1, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);

        // No gender coded value on the student — this is exactly the scenario that
        // used to hit the `if (genderIds.Length == 0) return withAge;` early-return
        // and leave CurrentGrade null.
        var handler = new ScriptedHandler()
            .Map("/students", Json(new[] { student }))
            .Map("/students/enrollments/by-students", Json(new[] { enrollment }))
            .Map("/students/grade-levels", Json(new[] { grade }));

        var client = CreateClient(handler);
        var result = await client.ListStudentsAsync();

        result.Should().NotBeNull();
        result!.Should().HaveCount(1);
        result[0].CurrentGrade.Should().NotBeNull("the grade enrichment must run even when the student has no gender");
        result[0].CurrentGrade!.Name.Should().Be("Grade 5");
    }

    [TestMethod]
    public async Task ListStudents_PopulatesCurrentGrade_WhenStudentHasGender()
    {
        var studentId = Guid.NewGuid();
        var gradeId = Guid.NewGuid();
        var genderId = Guid.NewGuid();

        var student = new StudentDto(
            Id: studentId, StudentNumber: "1002", FirstName: "John", LastName: "Smith",
            DateOfBirth: new DateOnly(2014, 5, 20), GenderCodedValueId: genderId, IsDeleted: false,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);
        var enrollment = new StudentEnrollmentDto(
            Id: Guid.NewGuid(), StudentId: studentId, PeriodId: Guid.NewGuid(), GradeLevelId: gradeId,
            GradeStrandCodedValueId: null, EnrolledOn: new DateOnly(2024, 9, 1), ExitDate: null,
            Status: "Active", CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);
        var grade = new GradeLevelDto(
            Id: gradeId, CodedValueId: Guid.NewGuid(), Level: 6, Name: "Grade 6", DisplayOrder: 1,
            TopicCount: 0, StudentCount: 1, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);

        var handler = new ScriptedHandler()
            .Map("/students", Json(new[] { student }))
            .Map("/students/enrollments/by-students", Json(new[] { enrollment }))
            .Map("/students/grade-levels", Json(new[] { grade }));

        // Gender lookup is hit when a student HAS a gender; both gender and grade
        // enrichment must coexist.
        var codedValuesHandler = new ScriptedHandler()
            .Map("/api/coded-values/by-ids", Json(new[]
            {
                new CodedValueDto(genderId, "F", "Female", null, null, null, false, 0,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    Array.Empty<CodedValueAttributeDto>(), Array.Empty<CodedValueAttributeDefinitionDto>()),
            }));

        var client = CreateClient(handler, codedValuesHandler);
        var result = await client.ListStudentsAsync();

        result.Should().NotBeNull();
        result!.Should().HaveCount(1);
        result[0].GenderName.Should().Be("Female");
        result[0].CurrentGrade.Should().NotBeNull();
        result[0].CurrentGrade!.Name.Should().Be("Grade 6");
    }

    [TestMethod]
    public async Task ListStudents_DoesNotShowCurrentGrade_WhenNoActiveEnrollment()
    {
        var studentId = Guid.NewGuid();
        var student = new StudentDto(
            Id: studentId, StudentNumber: "1003", FirstName: "Ada", LastName: "Lovelace",
            DateOfBirth: new DateOnly(2016, 2, 2), GenderCodedValueId: null, IsDeleted: false,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);

        // No enrollments at all -> CurrentGrade stays null (nothing to show).
        var handler = new ScriptedHandler()
            .Map("/students", Json(new[] { student }))
            .Map("/students/enrollments/by-students", Json(Array.Empty<StudentEnrollmentDto>()));

        var client = CreateClient(handler);
        var result = await client.ListStudentsAsync();

        result.Should().NotBeNull();
        result!.Should().HaveCount(1);
        result[0].CurrentGrade.Should().BeNull();
    }

    [TestMethod]
    public async Task ListStudents_UsesBulkEnrollmentEndpoint_NotNPlusOne()
    {
        // Regression: enrichment must fetch enrollments for ALL students in ONE
        // bulk request (GET /students/enrollments/by-students), never an N+1
        // per-student loop (GET /students/enrollments/by-student/{id}). This pins
        // the optimization against a future revert to per-student calls, which is
        // O(students) HTTP round-trips on the landing page.
        var gradeId = Guid.NewGuid();
        var students = Enumerable.Range(1, 3)
            .Select(i => new StudentDto(
                Id: Guid.NewGuid(), StudentNumber: $"10{i:00}", FirstName: $"S{i}", LastName: "Bulk",
                DateOfBirth: new DateOnly(2014, 3, 3), GenderCodedValueId: null, IsDeleted: false,
                CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow))
            .ToArray();
        var enrollments = students
            .Select(s => new StudentEnrollmentDto(
                Id: Guid.NewGuid(), StudentId: s.Id, PeriodId: Guid.NewGuid(), GradeLevelId: gradeId,
                GradeStrandCodedValueId: null, EnrolledOn: new DateOnly(2024, 9, 1), ExitDate: null,
                Status: "Active", CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow))
            .ToArray();
        var grade = new GradeLevelDto(
            Id: gradeId, CodedValueId: Guid.NewGuid(), Level: 4, Name: "Grade 4", DisplayOrder: 1,
            TopicCount: 0, StudentCount: 3, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow);

        var handler = new ScriptedHandler()
            .Map("/students", Json(students))
            .Map("/students/enrollments/by-students", Json(enrollments))
            .Map("/students/grade-levels", Json(new[] { grade }));

        var client = CreateClient(handler);
        var result = await client.ListStudentsAsync();

        result.Should().NotBeNull();
        result!.Should().HaveCount(3);
        result.Should().OnlyContain(s => s.CurrentGrade != null,
            "every student's CurrentGrade should be hydrated from the bulk enrollment fetch");

        // The bulk endpoint was hit exactly once; the per-student endpoint was
        // never hit. If this fails, enrichment regressed to an N+1 loop.
        handler.CallCounts["/students/enrollments/by-students"].Should().Be(1,
            "enrollments are bulk-loaded in a single round-trip");
        handler.CallCounts.Should().NotContainKey("/students/enrollments/by-student/" + students[0].Id,
            "enrichment must not fall back to per-student enrollment calls");
    }

    [TestMethod]
    public async Task ListStudents_PopulatesGuardianCount_FromBulkEndpoint()
    {
        var studentWithGuardians = Guid.NewGuid();
        var studentNoGuardians = Guid.NewGuid();

        var students = new[]
        {
            new StudentDto(
                Id: studentWithGuardians, StudentNumber: "2001", FirstName: "Grace", LastName: "Hopper",
                DateOfBirth: new DateOnly(2013, 4, 4), GenderCodedValueId: null, IsDeleted: false,
                CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow),
            new StudentDto(
                Id: studentNoGuardians, StudentNumber: "2002", FirstName: "Katherine", LastName: "Johnson",
                DateOfBirth: new DateOnly(2013, 5, 5), GenderCodedValueId: null, IsDeleted: false,
                CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow),
        };

        var handler = new ScriptedHandler()
            .Map("/students", Json(students))
            .Map("/students/enrollments/by-students", Json(Array.Empty<StudentEnrollmentDto>()))
            .Map("/students/guardian-counts", Json(new[]
            {
                new GuardianCountDto(studentWithGuardians, 2),
            }));

        var client = CreateClient(handler);
        var result = await client.ListStudentsAsync();

        result.Should().NotBeNull();
        result!.Should().HaveCount(2);

        var withGuardians = result.Single(s => s.Id == studentWithGuardians);
        withGuardians.GuardianCount.Should().Be(2, "the count is hydrated from the bulk guardian-counts endpoint");

        var noGuardians = result.Single(s => s.Id == studentNoGuardians);
        noGuardians.GuardianCount.Should().BeNull("a student absent from the count response keeps GuardianCount null");

        // The bulk endpoint was hit exactly once (no per-student N+1).
        handler.CallCounts["/students/guardian-counts"].Should().Be(1,
            "guardian counts are bulk-loaded in a single round-trip");
    }

    [TestMethod]
    public async Task ListGuardians_PopulatesStudentCount_AndTitleName_FromBulkEndpoints()
    {
        var guardianWithStudents = Guid.NewGuid();
        var guardianNoStudents = Guid.NewGuid();
        var titleId = Guid.NewGuid();

        var guardians = new[]
        {
            new GuardianDto(
                Id: guardianWithStudents, TitleCodedValueId: titleId, FirstName: "Grace", LastName: "Hopper",
                DisplayName: "Grace Hopper", Address: null, CommunityId: null, IsDeleted: false,
                CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow),
            new GuardianDto(
                Id: guardianNoStudents, TitleCodedValueId: null, FirstName: "Ada", LastName: "Lovelace",
                DisplayName: null, Address: null, CommunityId: null, IsDeleted: false,
                CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow),
        };

        var codedValue = new CodedValueDto(
            Id: titleId, Code: "mr", Name: "Mr", Description: null, ParentId: null, ParentCode: null,
            IsDisabled: false, DisplayOrder: 1, CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow,
            Attributes: System.Array.Empty<CodedValueAttributeDto>(),
            AttributeDefinitions: System.Array.Empty<CodedValueAttributeDefinitionDto>());

        var handler = new ScriptedHandler()
            .Map("/guardians", Json(guardians))
            .Map("/guardians/student-counts", Json(new[]
            {
                new StudentCountDto(guardianWithStudents, 2),
            }));
        var codedValuesHandler = new ScriptedHandler()
            .Map("/api/coded-values/by-ids", Json(new[] { codedValue }));

        var client = CreateClient(handler, codedValuesHandler);
        var result = await client.ListGuardiansAsync();

        result.Should().NotBeNull();
        result!.Should().HaveCount(2);

        var withStudents = result.Single(g => g.Id == guardianWithStudents);
        withStudents.StudentCount.Should().Be(2, "the count is hydrated from the bulk student-counts endpoint");
        withStudents.TitleName.Should().Be("Mr", "the salutation title is resolved from the title coded value");

        var noStudents = result.Single(g => g.Id == guardianNoStudents);
        noStudents.StudentCount.Should().BeNull("a guardian absent from the count response keeps StudentCount null");
        noStudents.TitleName.Should().BeNull("a guardian with no title coded value keeps TitleName null");

        // Each bulk endpoint was hit exactly once (no per-guardian N+1).
        handler.CallCounts["/guardians/student-counts"].Should().Be(1,
            "student counts are bulk-loaded in a single round-trip");
        codedValuesHandler.CallCounts["/api/coded-values/by-ids"].Should().Be(1,
            "salutation titles are bulk-loaded in a single round-trip");
    }
}
