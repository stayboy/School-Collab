using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Round <c>teacher-scope-auth</c> D2 / D6.3 (client half) — the teacher-scope HTTP port's
/// fail-CLOSED posture. Every failure mode the plan enumerates (non-2xx, transport failure,
/// malformed body, no body) must yield an EMPTY scope, never the tenant-wide
/// <see cref="TeacherScope.Unrestricted"/>; a teacher-only caller must never be widened by a
/// Students outage.
///
/// <para>Note: a scripted handler never follows redirects, so this file cannot by itself
/// discriminate <c>AllowAutoRedirect=false</c> — that stays the architecture guard's source
/// assertion on the migrated registration.</para>
/// </summary>
[TestClass]
public class TeacherScopeHttpClientTests
{
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid GradeId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid SubjectId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    private static TeacherScopeHttpClient Create(MockHttpMessageHandler handler) =>
        Create(handler, out _);

    private static TeacherScopeHttpClient Create(MockHttpMessageHandler handler, out Mock<IHttpClientFactory> factory)
    {
        factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("students-api"))
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("http://students-api") });
        return new TeacherScopeHttpClient(factory.Object, NullLogger<TeacherScopeHttpClient>.Instance);
    }

    [TestMethod]
    public async Task GetScopeAsync_NonSuccessStatus_FailsClosedToEmpty()
    {
        var handler = new MockHttpMessageHandler();
        handler.When("*/teachers/*").Respond(HttpStatusCode.InternalServerError);

        var scope = await Create(handler).GetScopeAsync(TeacherId, CancellationToken.None);

        scope.IsUnrestricted.Should().BeFalse("a Students error must never widen a teacher to tenant-wide");
        scope.IsEmpty.Should().BeTrue();
        scope.Taught.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetScopeAsync_TransportFailure_FailsClosedToEmpty()
    {
        var handler = new MockHttpMessageHandler();
        handler.When("*/teachers/*").Throw(new HttpRequestException("students-api unreachable"));

        var scope = await Create(handler).GetScopeAsync(TeacherId, CancellationToken.None);

        scope.IsUnrestricted.Should().BeFalse();
        scope.IsEmpty.Should().BeTrue();
    }

    [TestMethod]
    public async Task GetScopeAsync_MalformedBody_FailsClosedToEmpty()
    {
        var handler = new MockHttpMessageHandler();
        handler.When("*/teachers/*").Respond("application/json", "{ not json ]");

        var scope = await Create(handler).GetScopeAsync(TeacherId, CancellationToken.None);

        scope.IsUnrestricted.Should().BeFalse();
        scope.IsEmpty.Should().BeTrue();
    }

    [TestMethod]
    public async Task GetScopeAsync_EmptyTeacherId_IsEmptyWithoutCallingStudents()
    {
        var handler = new MockHttpMessageHandler();
        handler.When("*/teachers/*").Respond(HttpStatusCode.OK, "application/json", "[]");

        var scope = await Create(handler, out var factory).GetScopeAsync(Guid.Empty, CancellationToken.None);

        scope.IsEmpty.Should().BeTrue();
        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never,
            "an empty teacher id cannot resolve a scope, so no hop is attempted");
    }

    [TestMethod]
    public async Task GetScopeAsync_OkWithRows_MapsGradeAndSubjectMirror()
    {
        var handler = new MockHttpMessageHandler();
        handler.When("*/teachers/*").Respond("application/json",
            $$"""
            [
              { "rowId": "11111111-1111-1111-1111-111111111111", "gradeLevelId": "{{GradeId}}", "gradeName": "Grade 7",
                "gradeLevel": 7, "subjectId": "{{SubjectId}}", "subjectName": "Art", "roleCodedValueId": null },
              { "rowId": "22222222-2222-2222-2222-222222222222", "gradeLevelId": "{{GradeId}}", "gradeName": "Grade 7",
                "gradeLevel": 7, "subjectId": null, "subjectName": null, "roleCodedValueId": null }
            ]
            """);

        var scope = await Create(handler).GetScopeAsync(TeacherId, CancellationToken.None);

        scope.IsUnrestricted.Should().BeFalse();
        scope.IsEmpty.Should().BeFalse();
        scope.TeacherId.Should().Be(TeacherId);
        scope.Taught.Should().BeEquivalentTo(new[]
        {
            new TeacherSubjectGrade(GradeId, SubjectId, null),
            new TeacherSubjectGrade(GradeId, null, null),
        }, "the local mirror carries the grade and the optional subject through unchanged");
    }

    [TestMethod]
    public async Task GetScopeAsync_OkWithNoRows_IsResolvedNotUnrestricted()
    {
        var handler = new MockHttpMessageHandler();
        handler.When("*/teachers/*").Respond("application/json", "[]");

        var scope = await Create(handler).GetScopeAsync(TeacherId, CancellationToken.None);

        scope.IsUnrestricted.Should().BeFalse();
        scope.IsEmpty.Should().BeFalse("a 200 with no rows is a RESOLVED answer — the teacher teaches nothing");
        scope.Taught.Should().BeEmpty();
        scope.TeacherId.Should().Be(TeacherId,
            "the caller's own creations stay visible: the own-creation leg does not depend on the Students hop");
    }
}
