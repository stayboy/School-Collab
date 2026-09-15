using System;
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
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Families.Services;

namespace SchoolCollab.Families.Tests.Unit;

/// <summary>
/// WS-A5 (slice 2b) — the ward assignment-list surface. Binding coverage:
/// <list type="bullet">
///   <item><c>RendersRows_FromWardList</c> — the grid renders each assignment row.</item>
///   <item><c>HasLockedModules_Badge_Truth</c> — the Locked/Ready badge tracks the flag.</item>
///   <item><c>EmptyState_Shows_Message</c> — an empty list renders the empty message bar.</item>
///   <item><c>NoStudentId_Shows_Warning</c> — a missing ward id renders a warning.</item>
/// </list>
/// </summary>
[TestClass]
public class WardIndexBunitTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;
    private static readonly Guid StudentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AssignmentA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AssignmentB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<WardSubmissionStateDto>(), new JsonStringEnumConverter<ModuleTypeDto>() }
    };

    public WardIndexBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        _mockHttp = new MockHttpMessageHandler();
        var http = _mockHttp.ToHttpClient();
        http.BaseAddress = new Uri("http://localhost");
        Services.AddSingleton(http);
        Services.AddSingleton<FamiliesApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<FamiliesApiClient>>());
    }

    [TestMethod]
    public void RendersRows_FromWardList()
    {
        SetupList(new[]
        {
            new WardAssignmentListItemDto(AssignmentA, "Algebra", DateTimeOffset.UtcNow, WardSubmissionStateDto.InProgress, HasLockedModules: true),
            new WardAssignmentListItemDto(AssignmentB, "History", null, WardSubmissionStateDto.NotStarted, HasLockedModules: false)
        });

        var cut = Render<SchoolCollab.Families.Components.Pages.Ward.Index>(p => p.Add(x => x.StudentId, StudentId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Algebra"));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("History"));
    }

    [TestMethod]
    public void HasLockedModules_Badge_Truth()
    {
        SetupList(new[]
        {
            new WardAssignmentListItemDto(AssignmentA, "Assignment A", null, WardSubmissionStateDto.NotStarted, HasLockedModules: true),
            new WardAssignmentListItemDto(AssignmentB, "Assignment B", null, WardSubmissionStateDto.NotStarted, HasLockedModules: false)
        });

        var cut = Render<SchoolCollab.Families.Components.Pages.Ward.Index>(p => p.Add(x => x.StudentId, StudentId));
        cut.WaitForAssertion(() => cut.FindAll("fluent-badge").Count.Should().Be(2));

        // Map badge text to a boolean, keyed by each row's Assignment (title) so the
        // assertion is real: the row for the assignment with HasLockedModules=true must
        // show the Locked badge, and the other row Ready. The grid renders rows in item
        // order, so the Locked badge sits between Assignment A's title and Assignment B's
        // title; a per-row positional check catches an INVERTED mapping, which a plain
        // "the set contains both words" check would miss.
        var idxA = cut.Markup.IndexOf("Assignment A", StringComparison.Ordinal);
        var idxLocked = cut.Markup.IndexOf("Locked", StringComparison.Ordinal);
        var idxB = cut.Markup.IndexOf("Assignment B", StringComparison.Ordinal);
        var idxReady = cut.Markup.IndexOf("Ready", StringComparison.Ordinal);

        idxLocked.Should().BeInRange(idxA, idxB);   // Locked belongs to Assignment A's row
        idxReady.Should().BeGreaterThan(idxB);      // Ready belongs to Assignment B's row
    }

    [TestMethod]
    public void EmptyState_Shows_Message()
    {
        SetupList(System.Array.Empty<WardAssignmentListItemDto>());
        var cut = Render<SchoolCollab.Families.Components.Pages.Ward.Index>(p => p.Add(x => x.StudentId, StudentId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No assignments for this ward yet"));
    }

    [TestMethod]
    public void NoStudentId_Shows_Warning()
    {
        var cut = Render<SchoolCollab.Families.Components.Pages.Ward.Index>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No ward selected for this session"));
    }

    private void SetupList(WardAssignmentListItemDto[] items)
    {
        _mockHttp.When(HttpMethod.Get, $"http://localhost/students/{StudentId}/assignments")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(items, Json));
    }
}
