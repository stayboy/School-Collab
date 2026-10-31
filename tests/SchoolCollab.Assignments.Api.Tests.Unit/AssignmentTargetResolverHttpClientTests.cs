using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// The glue the audience panel's ordering depends on: <see cref="AssignmentTargetResolverHttpClient"/>
/// is the ONLY place the Students wire shape (<c>{ id, firstName, lastName }</c>) becomes the panel's
/// sort key <see cref="TargetStudent.FullName"/>. The handler tests prove the name SOURCE changed to
/// this read; they build <see cref="TargetStudent"/> fakes by hand, so they cannot catch a format
/// regression here (grill round 3 pinned <c>"{LastName}, {FirstName}"</c>; swapping to
/// <c>"{First} {Last}"</c> would silently invert the panel's only stated ordering) and they cannot
/// catch the <c>withNames=true</c> flag disappearing from the URL.
///
/// <para>Also pins the round's additive-contract claim from the caller side: the id-only method sends
/// NO <c>withNames</c> parameter, which is what keeps the response a bare <c>Guid[]</c> for every
/// pre-existing consumer.</para>
/// </summary>
[TestClass]
public class AssignmentTargetResolverHttpClientTests
{
    private static readonly Guid StudentA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StudentB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GradeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private const string ByTargetUrl = "http://students-api/students/by-target?gradeLevelIds=";

    private static AssignmentTargetResolverHttpClient NewClient(MockHttpMessageHandler studentsApi)
        => new(new SingleClientFactory(studentsApi), NullLogger<AssignmentTargetResolverHttpClient>.Instance);

    private static readonly IReadOnlyList<TargetConstraint> GradeTarget =
        [new TargetConstraint(TargetKind.GradeLevel, GradeId)];

    [TestMethod]
    public async Task ResolveStudents_FormatsFullNameAsSurnameThenFirstName()
    {
        // Arrange — the wire carries first/last separately; the panel sorts on "Surname, First".
        var studentsApi = new MockHttpMessageHandler();
        studentsApi.When($"{ByTargetUrl}{GradeId}&withNames=true")
            .Respond("application/json", JsonSerializer.Serialize(new[]
            {
                new { id = StudentA, firstName = "Tolu", lastName = "Adeyemi" },
                new { id = StudentB, firstName = "Ngozi", lastName = "Okafor" },
            }));

        // Act
        var students = await NewClient(studentsApi).ResolveStudentsAsync(GradeTarget, includeAllStudents: false);

        // Assert
        students.Should().HaveCount(2);
        students[0].Id.Should().Be(StudentA);
        students[0].FullName.Should().Be("Adeyemi, Tolu",
            "the panel sorts on this string; \"Tolu Adeyemi\" would silently reorder the whole table");
        students[1].FullName.Should().Be("Okafor, Ngozi");
        studentsApi.VerifyNoOutstandingExpectation();
    }

    [TestMethod]
    public async Task ResolveStudents_RequestsTheNamesFlag()
    {
        // Arrange — an unmatched URL throws in MockHttp, so this asserts the flag is on the wire.
        var studentsApi = new MockHttpMessageHandler();
        studentsApi.When($"{ByTargetUrl}{GradeId}&withNames=true")
            .Respond("application/json", "[]");

        // Act
        await NewClient(studentsApi).ResolveStudentsAsync(GradeTarget, includeAllStudents: false);

        // Assert
        studentsApi.VerifyNoOutstandingExpectation();
    }

    [TestMethod]
    public async Task ResolveStudentIds_SendsNoNamesFlag_SoTheResponseStaysAGuidArray()
    {
        // Arrange — the pre-existing contract: every consumer that came before the names layer reads
        // a bare Guid[]. If this URL ever grows withNames, the route silently switches response shape.
        var studentsApi = new MockHttpMessageHandler();
        studentsApi.When($"{ByTargetUrl}{GradeId}")
            .Respond("application/json", JsonSerializer.Serialize(new[] { StudentA, StudentB }));

        // Act
        var ids = await NewClient(studentsApi).ResolveStudentIdsAsync(GradeTarget, includeAllStudents: false);

        // Assert
        ids.Should().Equal(StudentA, StudentB);
        studentsApi.VerifyNoOutstandingExpectation();
    }

    [TestMethod]
    public async Task ResolveStudents_WithNoConstraintsAndNoAllStudents_ShortCircuits()
    {
        // Arrange — no handler is registered, so any round-trip would throw.
        var studentsApi = new MockHttpMessageHandler();

        // Act
        var students = await NewClient(studentsApi).ResolveStudentsAsync([], includeAllStudents: false);

        // Assert
        students.Should().BeEmpty("an empty target set resolves to nothing without a cross-context hop");
    }

    /// <summary>The resolver only ever calls the <c>students-api</c> client.</summary>
    private sealed class SingleClientFactory(MockHttpMessageHandler studentsApi) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = studentsApi.ToHttpClient();
            client.BaseAddress = new Uri("http://students-api");
            return client;
        }
    }
}
