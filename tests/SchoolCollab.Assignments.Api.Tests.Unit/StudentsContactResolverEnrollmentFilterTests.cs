using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// The subject-enrolment publish filter inside <see cref="StudentsContactResolver"/>
/// (subject-enrollment-requirement-flag.md FR-ER-13/15).
/// <para>Acceptance criterion 8(a): no <c>TopicId</c> ⇒ the eligibility hop is not called at
/// all and the cohort is untouched.</para>
/// <para>Acceptance criterion 8(b): a <c>false</c> subject on a grade-targeted cohort yields
/// exactly the pre-change recipient set.</para>
/// <para>Acceptance criterion 8(c): a <c>true</c> subject removes exactly
/// <c>Audience \ Eligible</c> per targeted grade, while a student who arrived on a
/// non-grade leg is untouched.</para>
/// <para>Acceptance criterion 9 (FR-ER-15): a failed eligibility call leaves that grade's
/// students unfiltered and logs a warning.</para>
/// </summary>
[TestClass]
public class StudentsContactResolverEnrollmentFilterTests
{
    private static readonly Guid GradeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondGradeId = Guid.Parse("11111111-1111-1111-1111-111111111112");
    private static readonly Guid TopicId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>A student in the targeted grade, not enrolled in the (explicit-enrolment) subject.</summary>
    private static readonly Guid GradeMemberId = Guid.Parse("33333333-3333-3333-3333-333333333331");

    /// <summary>A student who reached the cohort on a stream/student leg — never in any audience.</summary>
    private static readonly Guid StreamLegStudentId = Guid.Parse("33333333-3333-3333-3333-333333333332");

    private static readonly Guid SecondGradeMemberId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string StudentsApi = "http://students-api";
    private const string EligibilityUrl = $"{StudentsApi}/students/topic-enrollment";
    private const string SubscribedUrl = $"{StudentsApi}/contacts/subscribed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed class CapturingLogger : ILogger<StudentsContactResolver>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopScope();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    private static StudentsContactResolver NewResolver(MockHttpMessageHandler studentsApi, ILogger<StudentsContactResolver>? logger = null) =>
        new(new StubStudentsHttpClientFactory(studentsApi), logger ?? new CapturingLogger());

    private static ResolveSubscribersRequest Request(Guid? topicId, params Guid[] studentIds) =>
        new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), SubscriptionScope.AllAssignments,
            GradeLevelIds: [GradeId], StudentIds: studentIds, TopicId: topicId);

    private static void ArrangeCohort(MockHttpMessageHandler students)
    {
        // No guardians: one student-owned contact per student keeps the assertion about which
        // STUDENTS survive, not about contact fan-out.
        students.When($"{StudentsApi}/students/*/guardians").Respond("application/json", "[]");
        students.When(SubscribedUrl).Respond("application/json", JsonSerializer.Serialize(
            new[] { new SubscribedContactDto(Guid.NewGuid(), ContactChannel.Email, "ward@example.com", Role: null) },
            JsonOptions));
    }

    private static void ArrangeEligibility(MockHttpMessageHandler students, Guid[] audience, Guid[] eligible) =>
        students.When(EligibilityUrl)
            .WithQueryString($"gradeLevelId={GradeId}&topicId={TopicId}")
            .Respond("application/json", JsonSerializer.Serialize(new TopicEnrollmentDto(audience, eligible), JsonOptions));

    private static Guid[] StudentOwnerIds(IReadOnlyList<SubscriberInfo> subscribers) =>
        subscribers.Where(s => s.OwnerType == ContactOwnerType.Student)
            .Select(s => s.OwnerId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

    [TestMethod]
    public async Task WithoutATopic_TheEligibilityHopIsNeverCalled_AndTheCohortIsUnchanged()
    {
        var students = new MockHttpMessageHandler();
        ArrangeCohort(students);
        // No eligibility response is registered: MockHttp throws for an unmatched request, so a
        // hop would fail this test rather than pass silently.

        var subscribers = await NewResolver(students)
            .ResolveSubscribersAsync(Request(topicId: null, GradeMemberId, StreamLegStudentId));

        StudentOwnerIds(subscribers).Should().BeEquivalentTo([GradeMemberId, StreamLegStudentId],
            "a publish with no subject is not subject-scoped — it must behave exactly as before");
        students.GetMatchCount(students.When(EligibilityUrl))
            .Should().Be(0, "rule 1: no TopicId ⇒ no hop at all");
    }

    [TestMethod]
    public async Task FalseSubject_LeavesAGradeCohortIdenticalToPreChange()
    {
        var students = new MockHttpMessageHandler();
        ArrangeCohort(students);
        ArrangeEligibility(students, audience: [GradeMemberId, StreamLegStudentId],
            eligible: [GradeMemberId, StreamLegStudentId]);

        var beforeChange = await NewResolver(students)
            .ResolveSubscribersAsync(Request(topicId: null, GradeMemberId, StreamLegStudentId));
        var withFilter = await NewResolver(students)
            .ResolveSubscribersAsync(Request(TopicId, GradeMemberId, StreamLegStudentId));

        StudentOwnerIds(withFilter).Should().BeEquivalentTo(StudentOwnerIds(beforeChange),
            "Audience == Eligible for a false subject, so nothing is excludable");
        StudentOwnerIds(withFilter).Should().BeEquivalentTo([GradeMemberId, StreamLegStudentId]);
    }

    [TestMethod]
    public async Task TrueSubject_ExcludesOnlyGradeMembersMissingFromEligible()
    {
        var students = new MockHttpMessageHandler();
        ArrangeCohort(students);
        // The grade's audience holds one of the two students; the other came in on a stream leg
        // and is therefore in no audience array at all.
        ArrangeEligibility(students, audience: [GradeMemberId], eligible: []);

        var subscribers = await NewResolver(students)
            .ResolveSubscribersAsync(Request(TopicId, GradeMemberId, StreamLegStudentId));

        StudentOwnerIds(subscribers).Should().BeEquivalentTo([StreamLegStudentId],
            "Audience \\ Eligible excludes the grade member; a student in no audience is never removed");
    }

    [TestMethod]
    public async Task TrueSubject_WithAFailedEligibilityCall_KeepsTheCohortUnfiltered_AndLogsAWarning()
    {
        var students = new MockHttpMessageHandler();
        ArrangeCohort(students);
        students.When(EligibilityUrl)
            .WithQueryString($"gradeLevelId={GradeId}&topicId={TopicId}")
            .Respond(HttpStatusCode.InternalServerError);
        var logger = new CapturingLogger();

        var subscribers = await NewResolver(students, logger)
            .ResolveSubscribersAsync(Request(TopicId, GradeMemberId, StreamLegStudentId));

        StudentOwnerIds(subscribers).Should().BeEquivalentTo([GradeMemberId, StreamLegStudentId],
            "FR-ER-15: a transient error must not withhold work from a whole grade");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning,
            "the failure is logged, not swallowed");
    }

    [TestMethod]
    public async Task FailOpenIsPerGrade_SoOneFailedGradeDoesNotUnfilterTheOther()
    {
        var students = new MockHttpMessageHandler();
        ArrangeCohort(students);
        students.When(EligibilityUrl)
            .WithQueryString($"gradeLevelId={GradeId}&topicId={TopicId}")
            .Respond(HttpStatusCode.InternalServerError);
        students.When(EligibilityUrl)
            .WithQueryString($"gradeLevelId={SecondGradeId}&topicId={TopicId}")
            .Respond("application/json", JsonSerializer.Serialize(
                new TopicEnrollmentDto([SecondGradeMemberId], []), JsonOptions));

        var request = new ResolveSubscribersRequest(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), SubscriptionScope.AllAssignments,
            GradeLevelIds: [GradeId, SecondGradeId],
            StudentIds: [GradeMemberId, SecondGradeMemberId, StreamLegStudentId],
            TopicId: TopicId);

        var subscribers = await NewResolver(students).ResolveSubscribersAsync(request);

        StudentOwnerIds(subscribers).Should().BeEquivalentTo([GradeMemberId, StreamLegStudentId],
            "the failed grade contributes no exclusions; the healthy grade still excludes its unenrolled member");
    }

    private sealed class StubStudentsHttpClientFactory(MockHttpMessageHandler studentsApi) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = studentsApi.ToHttpClient();
            client.BaseAddress = new Uri(StudentsApi);
            return client;
        }
    }
}
