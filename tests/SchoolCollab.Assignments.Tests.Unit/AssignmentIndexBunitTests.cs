using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using IndexPage = SchoolCollab.Assignments.Admin.Components.Pages.Assignments.Index;
using SchoolCollab.Assignments.Admin.Services;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

[TestClass]
public class AssignmentIndexBunitTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;

    public AssignmentIndexBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        _apiJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter<AssignmentTypeDto>(), new JsonStringEnumConverter<AssignmentStatusDto>(), new JsonStringEnumConverter<GradingFormatDto>(), new JsonStringEnumConverter<TargetAudienceTypeDto>() }
        };

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<IndexPage>>());
    }

    private void SetupListResponse(AssignmentSummaryDto[] items)
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments*")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(items, _apiJsonOptions));
    }

    [TestMethod]
    public void Index_ShowsSpinner_WhileLoading()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments*")
            .Respond(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var cut = Render<IndexPage>();
        cut.Markup.ToLower().Should().Contain("progress");
    }

    [TestMethod]
    public void Index_ShowsEmptyMessage_WhenNoAssignments()
    {
        SetupListResponse([]);

        var cut = Render<IndexPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("No assignments yet");
        });
    }

    [TestMethod]
    public void Index_ShowsError_WhenApiFails()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments*")
            .Respond(HttpStatusCode.InternalServerError);

        var cut = Render<IndexPage>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().MatchRegex("Something went wrong|500");
        }, TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public void Index_JsonOptions_SerializeEnumsAsStrings()
    {
        var dto = new AssignmentSummaryDto(
            Guid.NewGuid(), "Test", null, AssignmentTypeDto.SemiManual,
            GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.AllStudents,
            Guid.NewGuid(), "Math", null, null, AssignmentStatusDto.Published,
            null, null, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(dto, _apiJsonOptions);

        json.Should().Contain("\"SemiManual\"");
        json.Should().Contain("\"Published\"");
        json.Should().NotContain("\"assignmentType\":1");
        json.Should().NotContain("\"status\":1");
    }

    [TestMethod]
    public void Index_JsonOptions_DeserializeStringsToEnums()
    {
        var json = """{"id":"00000000-0000-0000-0000-000000000001","title":"Test","description":null,"assignmentType":"SemiManual","gradingFormat":"TeacherGraded","targetAudienceType":"AllStudents","subjectCodedValueId":"00000000-0000-0000-0000-000000000002","subjectName":"Math","gradeCodedValueId":null,"gradeName":null,"status":"Published","dueDate":null,"maxScore":null,"createdByTeacherId":"00000000-0000-0000-0000-000000000003","createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-01T00:00:00+00:00"}""";

        var dto = JsonSerializer.Deserialize<AssignmentSummaryDto>(json, _apiJsonOptions);

        dto.Should().NotBeNull();
        dto!.AssignmentType.Should().Be(AssignmentTypeDto.SemiManual);
        dto.Status.Should().Be(AssignmentStatusDto.Published);
    }
}