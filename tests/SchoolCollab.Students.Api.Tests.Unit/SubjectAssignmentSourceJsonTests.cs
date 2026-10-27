using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Commands.AssignStudentTopic;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Api.Tests.Unit;

/// <summary>
/// <b>The wire shape of <c>AssignStudentTopic.SourceType</c> is its NAME.</b>
///
/// <para>The admin client has always posted <c>"sourceType": "IndividualAssignment"</c>. The Students API
/// registered no enum converter, so every enrol POST was rejected with
/// <c>400 — The JSON value could not be converted to …AssignStudentTopic. Path: $.sourceType</c>, and the
/// UI surfaced it as "'{subject}' could not be added to this student. Try again." The enrol action — the
/// whole point of the subject-enrolment feature — did not work end-to-end.</para>
///
/// <para><b>Why every existing test stayed green:</b> the bUnit band test asserts the client's serialized
/// body against a stub HTTP handler, so it can prove the client sends the name but can never exercise the
/// <i>server's</i> deserialization. The bug lived precisely in the seam between the two, and only a live
/// probe caught it. This test closes that seam by round-tripping through the host's own configured
/// serializer rather than a hand-built <see cref="JsonSerializerOptions"/> — a locally constructed options
/// object would have masked the defect by supplying the converter the app was missing.</para>
/// </summary>
[TestClass]
public class SubjectAssignmentSourceJsonTests
{
    private static WebApplicationFactory<Program> NewFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:students-db",
                "Host=localhost;Database=students_placeholder;Username=test;Password=test");
            builder.UseSetting("ConnectionStrings:settings-db",
                "Host=localhost;Database=settings_placeholder;Username=test;Password=test");
            builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");
            builder.UseSetting("Outbox:ExchangeName", "students");
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        });

    [TestMethod]
    public void TheHostSerializer_DeserializesTheClientsStringSourceType()
    {
        using var factory = NewFactory();
        var json = factory.Services
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;

        const string body = """
            { "studentId": "11111111-1111-1111-1111-111111111111",
              "topicId":   "22222222-2222-2222-2222-222222222222",
              "periodId":  "33333333-3333-3333-3333-333333333333",
              "isOverride": false,
              "sourceType": "IndividualAssignment" }
            """;

        var command = JsonSerializer.Deserialize<AssignStudentTopic>(body, json);

        command.Should().NotBeNull(
            "the client's payload shape must bind — this threw a JsonException before the converter was registered");
        command!.SourceType.Should().Be(SubjectAssignmentSource.IndividualAssignment,
            "the string the admin client sends must map to the enum member of the same name");
    }

    [TestMethod]
    public void TheHostSerializer_WritesTheNameNotTheOrdinal()
    {
        using var factory = NewFactory();
        var json = factory.Services
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;

        var command = new AssignStudentTopic(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), IsOverride: false,
            SubjectAssignmentSource.IndividualAssignment);

        JsonSerializer.Serialize(command, json).Should().Contain("\"IndividualAssignment\"",
            "a numeric ordinal would round-trip with no converter at all, so the name is the discriminating assertion");
    }
}
