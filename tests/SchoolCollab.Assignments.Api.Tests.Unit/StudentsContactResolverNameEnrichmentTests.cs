using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RichardSzalay.MockHttp;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// Round notification-templates-b1, spec §3.4: <see cref="StudentsContactResolver"/> maps the
/// Students API's subscription-response names onto <see cref="SubscriberInfo"/> — the ward id
/// rides the <b>same</b> <c>contacts/subscribed</c> call (no second hop), and a response without
/// names degrades to <see cref="string.Empty"/> instead of failing the publish.
/// </summary>
[TestClass]
public class StudentsContactResolverNameEnrichmentTests
{
    private static readonly Guid StudentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GuardianId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid StudentContactId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ResolveSubscribersRequest Request = new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        SubscriptionScope.AllAssignments,
        StudentIds: [StudentId]);

    private static string GuardiansUrl => $"http://students-api/students/{StudentId}/guardians";

    private const string SubscribedUrl = "http://students-api/contacts/subscribed";

    /// <summary>The exact query the resolver must send — including the ward id (§3.4): a call
    /// without it could not name a guardian's ward without a second hop.</summary>
    private static string SubscribedQuery(ContactOwnerType ownerType, Guid ownerId) =>
        $"ownerType={(int)ownerType}&ownerId={ownerId}"
        + $"&scope={(int)SubscriptionScope.AllAssignments}&studentId={StudentId}";

    private static string GuardiansJson() => JsonSerializer.Serialize(
        new[]
        {
            new StudentGuardianViewDto(
                GuardianId, StudentId, GuardianRole.Primary, null, false,
                "Kwame", "Mensah", "Mr. Mensah"),
        },
        JsonOptions);

    private static StudentsContactResolver NewResolver(MockHttpMessageHandler studentsApi) =>
        new(new StubStudentsHttpClientFactory(studentsApi), NullLogger<StudentsContactResolver>.Instance);

    [TestMethod]
    public async Task GuardianRow_CarriesContactNameAndWardName_FromTheSameSubscriptionCall()
    {
        var students = new MockHttpMessageHandler();
        students.When(GuardiansUrl).Respond("application/json", GuardiansJson());
        students.When(SubscribedUrl)
            .WithExactQueryString(SubscribedQuery(ContactOwnerType.Guardian, GuardianId))
            .Respond("application/json", JsonSerializer.Serialize(
                new[]
                {
                    new SubscribedContactDto(
                        ContactId, ContactChannel.Email, "mensah@example.com", GuardianRole.Primary,
                        ContactName: "Mr. Mensah", StudentName: "Ada Mensah"),
                },
                JsonOptions));
        students.When(SubscribedUrl)
            .WithExactQueryString(SubscribedQuery(ContactOwnerType.Student, StudentId))
            .Respond("application/json", JsonSerializer.Serialize(
                new[]
                {
                    new SubscribedContactDto(
                        StudentContactId, ContactChannel.Email, "ada@example.com", Role: null,
                        ContactName: null, StudentName: "Ada Mensah"),
                },
                JsonOptions));

        var subscribers = await NewResolver(students).ResolveSubscribersAsync(Request);

        var guardian = subscribers.Single(s => s.OwnerType == ContactOwnerType.Guardian);
        guardian.ContactName.Should().Be("Mr. Mensah");
        guardian.StudentName.Should().Be("Ada Mensah");

        var student = subscribers.Single(s => s.OwnerType == ContactOwnerType.Student);
        student.ContactName.Should().BeEmpty("a student-owned contact carries no guardian name");
        student.StudentName.Should().Be("Ada Mensah");
    }

    [TestMethod]
    public async Task ResponseWithoutNames_DegradesToEmptyStrings_AndStillResolves()
    {
        var students = new MockHttpMessageHandler();
        students.When(GuardiansUrl).Respond("application/json", GuardiansJson());
        // A producer that does not populate the enrichment fields (or sends explicit nulls) must
        // not break resolution — every name degrades to "" (spec §3.4).
        students.When(SubscribedUrl)
            .WithQueryString(SubscribedQuery(ContactOwnerType.Guardian, GuardianId))
            .Respond("application/json", JsonSerializer.Serialize(
                new[]
                {
                    new SubscribedContactDto(ContactId, ContactChannel.Email, "mensah@example.com", GuardianRole.Primary),
                },
                JsonOptions));
        students.When(SubscribedUrl)
            .WithQueryString(SubscribedQuery(ContactOwnerType.Student, StudentId))
            .Respond(
                "application/json",
                """[{"id":"44444444-4444-4444-4444-444444444444","channel":0,"value":"ada@example.com","role":null,"contactName":null,"studentName":null}]""");

        var subscribers = await NewResolver(students).ResolveSubscribersAsync(Request);

        subscribers.Should().HaveCount(2);
        subscribers.Should().OnlyContain(s => s.ContactName == string.Empty && s.StudentName == string.Empty);
        subscribers.Select(s => s.ContactId).Should().Contain(ContactId).And.Contain(StudentContactId);
    }

    private sealed class StubStudentsHttpClientFactory(MockHttpMessageHandler studentsApi) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = studentsApi.ToHttpClient();
            client.BaseAddress = new Uri("http://students-api");
            return client;
        }
    }
}
