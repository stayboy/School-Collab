using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Components.Pages.ActivityGroups;
using SchoolCollab.Students.Application.Services;
using System.Security.Claims;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the Activity Groups admin pages (spec activity-group-enrollment.md
/// Phase 4, FR-24..27). Verifies the list page renders groups from the API, the
/// details page renders the members tab, and the FeatureFlagGate hides the pages
/// while FEATURE:EnableActivityGroups is off (NFR-11).
/// </summary>
[TestClass]
public class ActivityGroupsPageTests : BunitContext
{
    public ActivityGroupsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new(StringComparer.OrdinalIgnoreCase);

        public ScriptedHandler Map(string url, HttpStatusCode status, string body)
        {
            _responses[url] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            foreach (var kv in _responses)
            {
                if (url.Equals(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(kv.Value.Status)
                    {
                        Content = new StringContent(kv.Value.Body, Encoding.UTF8, "application/json"),
                    });
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {url}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubFlagService : IFeatureFlagService
    {
        public bool Enabled { get; set; }
        public bool IsEnabled(string featureKey) => Enabled;
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(Enabled);
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private sealed class StubFlagNotifier : IFeatureFlagChangeNotifier
    {
        public event Action? FeatureFlagsChanged;
        public void Raise() => FeatureFlagsChanged?.Invoke();
    }

    private sealed class FakeAuth : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim("tenant_name", "Hydeson") }, "TestScheme"))));
    }

    private static readonly Guid GroupId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private (ScriptedHandler Handler, StubFlagService Flags) RegisterWith(params (string UrlPrefix, HttpStatusCode Status, string Body)[] responses)
    {
        var auth = new FakeAuth();
        var handler = new ScriptedHandler();
        foreach (var (url, status, body) in responses)
        {
            handler.Map(url, status, body);
        }
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };

        var flags = new StubFlagService { Enabled = true };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));
        Services.AddSingleton<IFeatureFlagService>(flags);
        Services.AddSingleton<IFeatureFlagChangeNotifier>(new StubFlagNotifier());
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));

        return (handler, flags);
    }

    private static string GroupsJson() => JsonSerializer.Serialize(new[]
    {
        new
        {
            Id = GroupId,
            Name = "Chess Club",
            Description = "After-school chess",
            Category = "Games",
            Capacity = 20,
            IsActive = true,
            EligibleGradeIds = Array.Empty<Guid>(),
            ActiveMemberCount = 3,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        },
    });

    private static string GroupJson() => JsonSerializer.Serialize(new
    {
        Id = GroupId,
        Name = "Chess Club",
        Description = "After-school chess",
        Category = "Games",
        Capacity = 20,
        IsActive = true,
        EligibleGradeIds = Array.Empty<Guid>(),
        ActiveMemberCount = 3,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    });

    private static string MembersJson() => JsonSerializer.Serialize(new[]
    {
        new
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ActivityGroupId = GroupId,
            StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            StudentName = "Alice Smith",
            JoinedOn = new DateOnly(2026, 1, 15),
            ExitedOn = (DateOnly?)null,
            Status = "Active",
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        },
    });

    [TestMethod]
    public void ListPage_RendersGroupsFromApi()
    {
        RegisterWith(
            ("/activity-groups", HttpStatusCode.OK, GroupsJson()),
            ("/students/grade-levels/landing", HttpStatusCode.OK, "[]"));

        var cut = Render<ActivityGroups>();
        cut.WaitForState(() => cut.Markup.Contains("Chess Club"), TimeSpan.FromSeconds(2));

        cut.Markup.Should().Contain("Chess Club");
        cut.Markup.Should().Contain("Games");
        cut.Markup.Should().Contain("Active");
        cut.Markup.Should().Contain("3"); // member count
        // AC-1c: a successful load renders with no error bar.
        cut.Markup.Should().NotContain("Could not load activity groups");
    }

    /// <summary>
    /// AC-1a/1b: when the list API fails (404 here), the page shows the red
    /// error message (via LandingPage Error), the empty-state bar reads "Could
    /// not load activity groups.", and the loading spinner stops (no
    /// fluent-progress-ring) instead of spinning forever.
    /// </summary>
    [TestMethod]
    public void ListPage_ApiFailure_ShowsErrorAndStopsSpinner()
    {
        // No mapped URL: GET /activity-groups returns 404 → GetFromJsonAsync
        // throws → the ReloadAsync catch path runs. /students/grade-levels/landing
        // is never reached.
        RegisterWith();

        var cut = Render<ActivityGroups>();
        cut.WaitForState(() => cut.Markup.Contains("Could not load activity groups"), TimeSpan.FromSeconds(2));

        cut.Markup.Should().Contain("Could not load activity groups.", "AC-1b: empty state reads as a failure, not an empty list");
        cut.Markup.Should().Contain("404", "AC-1a: the rendered error text includes the failure (HttpRequestException message)");
        cut.Markup.Should().NotContain("fluent-progress-ring", "AC-1b: the loading spinner stops on failure");
    }

    [TestMethod]
    public void ListPage_HiddenWhenFlagOff()
    {
        var (_, flags) = RegisterWith(("/activity-groups", HttpStatusCode.OK, GroupsJson()));
        flags.Enabled = false;

        var cut = Render<ActivityGroups>();

        // FeatureFlagGate hides the LandingPage content while the flag is off,
        // so the page renders no content (markup stays empty). There is no
        // longer an always-rendered inline <style> block to make markup
        // non-empty, so we assert directly on the hidden content.
        cut.Markup.Should().NotContain("Chess Club");
        cut.Markup.Should().NotContain("New Activity Group");
    }

    [TestMethod]
    public void DetailsPage_RendersMembersTab()
    {
        RegisterWith(
            ($"/activity-groups/{GroupId}/members", HttpStatusCode.OK, MembersJson()),
            ($"/activity-groups/{GroupId}", HttpStatusCode.OK, GroupJson()));

        var cut = Render<ActivityGroupDetails>(p => p.Add(x => x.Id, GroupId));
        cut.WaitForState(() => cut.Markup.Contains("Alice Smith"), TimeSpan.FromSeconds(2));

        cut.Markup.Should().Contain("Alice Smith");
        cut.Markup.Should().Contain("Members");
        cut.Markup.Should().Contain("Remove");
    }

    /// <summary>
    /// B1 (AC-38): the Roll over button renders only for a non-OpenEnded active
    /// group. The button lives in the page-toolbar SectionContent, so the page
    /// is hosted under a component that provides the matching SectionOutlet.
    /// Render-level assertion only — no confirmation-dialog driving.
    /// </summary>
    [TestMethod]
    public void DetailsPage_RolloverButton_HiddenForOpenEnded()
    {
        RegisterWith(
            ($"/activity-groups/{GroupId}/members", HttpStatusCode.OK, MembersJson()),
            ($"/activity-groups/{GroupId}", HttpStatusCode.OK, GroupJsonWithSpan("OpenEnded")));

        var cut = Render<RolloverHost>(p => p.Add(x => x.GroupId, GroupId));
        cut.WaitForState(() => cut.Markup.Contains("After-school chess"), TimeSpan.FromSeconds(2));
        cut.Markup.Should().NotContain("Roll over", "an OpenEnded group has no rollover");
    }

    /// <summary>
    /// B1 (AC-38): a non-OpenEnded active group renders the Roll over button.
    /// </summary>
    [TestMethod]
    public void DetailsPage_RolloverButton_ShownForDateRange()
    {
        RegisterWith(
            ($"/activity-groups/{GroupId}/members", HttpStatusCode.OK, MembersJson()),
            ($"/activity-groups/{GroupId}", HttpStatusCode.OK, GroupJsonWithSpan("DateRange")));

        var cut = Render<RolloverHost>(p => p.Add(x => x.GroupId, GroupId));
        cut.WaitForState(() => cut.Markup.Contains("Roll over"), TimeSpan.FromSeconds(2));
        cut.Markup.Should().Contain("Roll over", "a DateRange group offers rollover");
    }

    private static string GroupJsonWithSpan(string span) => JsonSerializer.Serialize(new
    {
        Id = GroupId,
        Name = "Chess Club",
        Description = "After-school chess",
        Category = "Games",
        Capacity = 20,
        IsActive = true,
        Span = span,
        EnrollmentStartDate = (DateOnly?)null,
        EnrollmentEndDate = (DateOnly?)null,
        AutoRenewDefault = true,
        EligibleGradeIds = Array.Empty<Guid>(),
        ActiveMemberCount = 3,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    });

    /// <summary>
    /// Test host that renders the page-toolbar SectionOutlet so the details
    /// page's toolbar (including the Roll over button) renders in bUnit.
    /// </summary>
    private sealed class RolloverHost : ComponentBase
    {
        [Parameter] public Guid GroupId { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<SectionOutlet>(0);
            builder.AddAttribute(1, "SectionName", "page-toolbar");
            builder.CloseComponent();

            builder.OpenComponent<ActivityGroupDetails>(2);
            builder.AddAttribute(3, "Id", GroupId);
            builder.CloseComponent();
        }
    }
}

