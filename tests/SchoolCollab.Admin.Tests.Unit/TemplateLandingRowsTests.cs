using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The optimistic reset mutation (`blazor-components.md` § "Landing-page performance pattern", rule 2):
/// the landing drops the row from the in-memory list rather than re-reading it, so the operator's sort
/// and scroll survive the action. The reset dialog is not renderable in bUnit, so this seam is where
/// the behaviour is proven.
/// </summary>
[TestClass]
public class TemplateLandingRowsTests
{
    private static TenantNotificationTemplateDto Row(Guid id, string subject) =>
        new(id, NotificationTemplateKind.Publish, NotificationChannel.Email, subject, "<p>b</p>", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, TenantId: null, OwnerName: "System");

    [TestMethod]
    public void WithoutReset_RemovesOnlyTheResetRow_AndKeepsOrder()
    {
        var a = Row(Guid.NewGuid(), "A");
        var b = Row(Guid.NewGuid(), "B");
        var c = Row(Guid.NewGuid(), "C");

        var remaining = TemplateLandingRows.WithoutReset([a, b, c], b.Id);

        remaining.Select(r => r.Subject).Should().Equal(["A", "C"],
            "the reset row leaves and the grid keeps its order (rule 2: no re-read)");
    }

    [TestMethod]
    public void WithoutReset_IsANoOp_ForAnUnknownId()
    {
        var a = Row(Guid.NewGuid(), "A");

        TemplateLandingRows.WithoutReset([a], Guid.NewGuid()).Should().ContainSingle();
    }

    [TestMethod]
    public void WithoutReset_HandlesTheLastRow()
    {
        var a = Row(Guid.NewGuid(), "A");

        TemplateLandingRows.WithoutReset([a], a.Id).Should().BeEmpty(
            "resetting the only row leaves the empty-state message, not a stale grid");
    }
}
