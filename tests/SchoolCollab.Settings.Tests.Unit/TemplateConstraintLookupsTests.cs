using System.Net;
using System.Text;
using FluentAssertions;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Degradation policy of <see cref="TemplateConstraintLookups"/> — the class that decides what the
/// template editor's constraint pickers may offer.
/// <para>
/// The defect these pin (owner-reported, 2026-10-23): one lookup's absence took the other three down
/// with it. <c>FEATURE:EnableActivityGroups</c> is off by default and <c>StudentEndpoints</c> maps
/// <c>/activity-groups</c> only when it is on, so the route answers 404 — and the single try/catch
/// around all four lookups turned that into an empty, disabled picker set plus a warning that read as
/// though every constraint name had been lost. The three cases below are the whole policy: all four
/// healthy, one route absent (a posture), one lookup failing (a fault).
/// </para>
/// </summary>
[TestClass]
public class TemplateConstraintLookupsTests
{
    private const string GradeLevelsJson =
        """[{ "id": "11111111-1111-1111-1111-111111111111", "name": "Grade 8" }]""";

    private const string TopicsJson =
        """[{ "id": "33333333-3333-3333-3333-333333333333", "name": "Mathematics" }]""";

    private const string ActivityGroupsJson =
        """
        [
          { "id": "44444444-4444-4444-4444-444444444444", "name": "Chess club", "isActive": true },
          { "id": "55555555-5555-5555-5555-555555555555", "name": "Archived club", "isActive": false }
        ]
        """;

    private const string StreamsJson =
        """
        [{
          "id": "eeeeeeee-0000-0000-0000-00000000000e", "code": "NORTH", "name": "North",
          "description": null, "parentId": null, "parentCode": "GRSTREAMS", "isDisabled": false,
          "displayOrder": 0, "createdAt": "2026-10-01T00:00:00+00:00", "updatedAt": "2026-10-01T00:00:00+00:00",
          "attributes": [], "attributeDefinitions": []
        }]
        """;

    /// <summary>Routes one path to a status, so a single case can be made absent or made to fail.</summary>
    private sealed class RoutingHandler(HttpStatusCode activityGroupsStatus) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);

            if (path == "/activity-groups" && activityGroupsStatus != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(activityGroupsStatus)
                {
                    Content = new StringContent(
                        activityGroupsStatus == HttpStatusCode.NotFound ? "no endpoint matched" : "boom",
                        Encoding.UTF8,
                        "application/json"),
                });
            }

            var body = path switch
            {
                "/students/grade-levels" => GradeLevelsJson,
                "/students/topics" => TopicsJson,
                "/activity-groups" => ActivityGroupsJson,
                "/api/coded-values/by-parent" => StreamsJson,
                _ => null,
            };

            if (body is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static async Task<TemplateConstraintLookups> LoadAsync(
        HttpStatusCode activityGroupsStatus = HttpStatusCode.OK)
    {
        var handler = new RoutingHandler(activityGroupsStatus);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var lookups = new TemplateConstraintLookups(
            new StudentsLookupClient(http), new CodedValuesApiClient(http));

        await lookups.LoadAsync();
        return lookups;
    }

    [TestMethod]
    public async Task WhenEveryLookupAnswers_EveryDimensionLoads_AndNothingIsReported()
    {
        // Act
        var lookups = await LoadAsync();

        // Assert
        lookups.Loaded.Should().BeTrue();
        lookups.Error.Should().BeNull("nothing failed");
        lookups.IsAvailable(TemplateConstraintDimension.GradeLevel).Should().BeTrue();
        lookups.IsAvailable(TemplateConstraintDimension.Subject).Should().BeTrue();
        lookups.IsAvailable(TemplateConstraintDimension.Stream).Should().BeTrue();
        lookups.IsAvailable(TemplateConstraintDimension.ActivityGroup).Should().BeTrue();
        lookups.IsOffered(TemplateConstraintDimension.ActivityGroup).Should().BeTrue();

        lookups.Grades.Should().ContainSingle().Which.Text.Should().Be("Grade 8");
        lookups.Subjects.Should().ContainSingle().Which.Text.Should().Be("Mathematics");
        lookups.Streams.Should().ContainSingle().Which.Text.Should().Be("North");
        lookups.Groups.Should().ContainSingle("the archived group is never offered — it could never match")
            .Which.Text.Should().Be("Chess club");
    }

    /// <summary>The owner-reported defect: the flag-off 404 must cost exactly one dimension.</summary>
    [TestMethod]
    public async Task WhenTheActivityGroupRouteIsNotMapped_OnlyThatDimensionIsLost_AndNothingIsReported()
    {
        // Act
        var lookups = await LoadAsync(HttpStatusCode.NotFound);

        // Assert — the three that exist are untouched…
        lookups.Loaded.Should().BeTrue();
        lookups.Grades.Should().ContainSingle();
        lookups.Subjects.Should().ContainSingle();
        lookups.Streams.Should().ContainSingle();

        // …the absent one is not offered, and is not dressed up as a fault.
        lookups.IsAvailable(TemplateConstraintDimension.ActivityGroup).Should().BeFalse();
        lookups.IsOffered(TemplateConstraintDimension.ActivityGroup).Should().BeFalse(
            "a route this host does not map is a feature the school does not have — the menu must not "
            + "advertise it");
        lookups.Error.Should().BeNull(
            "the old code reported a flag-off feature as an outage, which read as though every "
            + "constraint name had been lost");
        lookups.UnavailableReason(TemplateConstraintDimension.ActivityGroup)
            .Should().Contain("not available for this school");

        // A stored value for the absent dimension still renders, as its raw text — never dropped.
        lookups.DisplayFor(TemplateConstraintDimension.ActivityGroup, "dddddddd-0000-0000-0000-00000000000d")
            .Should().Be("dddddddd-0000-0000-0000-00000000000d");
    }

    /// <summary>A genuine outage loses its own dimension only, and IS reported.</summary>
    [TestMethod]
    public async Task WhenALookupFails_TheOtherDimensionsStillLoad_AndTheOutageIsReported()
    {
        // Act
        var lookups = await LoadAsync(HttpStatusCode.InternalServerError);

        // Assert
        lookups.Loaded.Should().BeTrue();
        lookups.Grades.Should().ContainSingle("grades loaded — the failure was activity groups");
        lookups.Subjects.Should().ContainSingle();
        lookups.Streams.Should().ContainSingle();

        lookups.IsAvailable(TemplateConstraintDimension.ActivityGroup).Should().BeFalse();
        lookups.IsOffered(TemplateConstraintDimension.ActivityGroup).Should().BeTrue(
            "an outage is transient, so the capability stays visible and disabled with its reason "
            + "rather than silently disappearing");
        lookups.Error.Should().NotBeNull("an outage is reported");
        lookups.Error.Should().Contain("Activity group", "the message names the dimension that failed");
    }

    /// <summary>Nothing is offered before the pass has run, so a picker can never be shown unfilled.</summary>
    [TestMethod]
    public void BeforeTheLoadPass_NothingIsAvailableOrOffered()
    {
        // Arrange
        var http = new HttpClient(new RoutingHandler(HttpStatusCode.OK)) { BaseAddress = new Uri("http://localhost") };
        var lookups = new TemplateConstraintLookups(new StudentsLookupClient(http), new CodedValuesApiClient(http));

        // Assert
        lookups.Loaded.Should().BeFalse();
        lookups.IsAvailable(TemplateConstraintDimension.GradeLevel).Should().BeFalse();
        lookups.IsOffered(TemplateConstraintDimension.GradeLevel).Should().BeFalse();
    }
}
