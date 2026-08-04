using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Admin.Services;

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

        public ScriptedHandler Map(string path, string body)
        {
            _responses[path] = body;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
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
            .Map($"/students/enrollments/by-student/{studentId}", Json(new[] { enrollment }))
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
            .Map($"/students/enrollments/by-student/{studentId}", Json(new[] { enrollment }))
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
            .Map($"/students/enrollments/by-student/{studentId}", Json(Array.Empty<StudentEnrollmentDto>()));

        var client = CreateClient(handler);
        var result = await client.ListStudentsAsync();

        result.Should().NotBeNull();
        result!.Should().HaveCount(1);
        result[0].CurrentGrade.Should().BeNull();
    }
}
