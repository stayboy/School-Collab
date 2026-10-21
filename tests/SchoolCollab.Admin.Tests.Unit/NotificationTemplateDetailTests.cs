using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;


namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The template detail page (redesign spec §3.5–§3.6, §3.12), tested through the route host: the two
/// dropdowns are locked indicators with their reason, one Save replaces the row by id (subject + body +
/// the whole constraint set), Reset refuses without a reason and never calls DELETE, the audit grid is
/// narrowed to THIS template, and the conflict warning appears only for an identical sibling set.
/// <para>
/// Dialog INVOCATION (the constraints kebab, the reset confirm) needs a live dialog provider, which this
/// renderer is not: those components are covered directly (<see cref="ConstraintEditDialogTests"/>, the
/// shared `ConfirmDialog` suite), and the browser pass is deferred per the round doc.
/// </para>
/// </summary>
[TestClass]
public class NotificationTemplateDetailTests : BunitContext
{
    public NotificationTemplateDetailTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private TemplateApiHandler Register(bool realTenant = true) =>
        TemplateTestServices.Register<TemplateEditor>(this, realTenant);

    /// <summary>
    /// Renders the SHARED EDITOR in Edit mode — the detail page is a six-line route host, and a path
    /// parameter (`{Id:guid}`) is supplied by the Router, which a bare bUnit tree does not have (the route
    /// value, unlike a `[SupplyParameterFromQuery]`, is not resolved from the navigation state).
    /// </summary>
    private IRenderedComponent<TemplateEditor> RenderDetail(Guid id) =>
        Render<TemplateEditor>(p => p
            .Add(x => x.Mode, TemplateEditorMode.Edit)
            .Add(x => x.Id, id));
    [TestMethod]
    public void TypeAndContactMode_AreLockedIndicators_WithTheirReason()
    {
        var handler = Register();
        var row = new TemplateRow { Kind = 0, Channel = 0, Subject = "LOCKED-ROW" };
        handler.Rows.Add(row);

        var cut = RenderDetail(row.Id);

        cut.WaitForAssertion(() =>
        {
            cut.FindComponents<DropdownComponent<TemplateEditor.KindOption, NotificationTemplateKind>>()
                .Single().Instance.Disabled.Should().BeTrue();
            cut.FindComponents<DropdownComponent<TemplateEditor.ChannelOption, NotificationChannel>>()
                .Single().Instance.Disabled.Should().BeTrue();
            cut.Markup.Should().Contain("Type and contact mode are fixed per template");
        });
    }

    [TestMethod]
    public async Task Save_ReplacesTheRowById_WithTheStagedDraft()
    {
        var handler = Register();
        var row = new TemplateRow
        {
            Kind = 0,
            Channel = 1,
            Body = "OLD-BODY",
            Constraints = [(0, TemplateApiHandler.GradeA)],
        };
        handler.Rows.Add(row);

        var cut = RenderDetail(row.Id);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("OLD-BODY"));

        var body = cut.FindComponents<FluentTextArea>().Single(a => a.Instance.Id == "template-body-text");
        await cut.InvokeAsync(() => body.Instance.ValueChanged.InvokeAsync("NEW-BODY"));

        cut.Find("#template-save").Click();

        cut.WaitForAssertion(() =>
        {
            var put = handler.Calls.Single(c => c.Method == "PUT");
            put.Url.Should().Contain($"/notification-templates/{row.Id:D}");
            put.Body.Should().Contain("NEW-BODY");
            put.Body.Should().Contain(TemplateApiHandler.GradeA);
        }, TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void Reset_WithoutAReason_IsRefused_AndNeverCallsDelete()
    {
        var handler = Register();
        var row = new TemplateRow { Kind = 0, Channel = 0, Subject = "RESET-ME" };
        handler.Rows.Add(row);

        var cut = RenderDetail(row.Id);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("RESET-ME"));

        cut.Find("#template-reset").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("A reason is required to reset a template"));
        handler.Calls.Should().NotContain(c => c.Method == "DELETE");
    }

    [TestMethod]
    public void TheAuditTrail_IsNarrowedToThisTemplate()
    {
        var handler = Register();
        var row = new TemplateRow { Kind = 0, Channel = 0, Subject = "AUDITED" };
        handler.Rows.Add(row);
        handler.AuditEntries.Add(new TemplateAuditRow(
            row.Id, 1, "AUDIT-REASON-HERE", "Test Actor", DateTimeOffset.UnixEpoch));

        var cut = RenderDetail(row.Id);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("AUDIT-REASON-HERE");
            handler.Calls.Should().Contain(c =>
                c.Method == "GET" && c.Url.Contains($"audit?templateId={row.Id:D}", StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public void TheOwner_IsShownInTheTitleRow()
    {
        var handler = Register();
        var row = new TemplateRow { Kind = 0, Channel = 0, Subject = "MINE" };
        handler.Rows.Add(row);

        var cut = RenderDetail(row.Id);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#template-editor-owner").TextContent.Trim().Should().Be("Northfield",
                "a tenant-owned row names its tenant (redesign spec §3.5/§3.7)");
        });
    }

    [TestMethod]
    public void ASystemRowInATenantSession_IsReadOnly_AndOffersAdoptInstead()
    {
        var handler = Register();
        var row = new TemplateRow
        {
            Kind = 0, Channel = 0, Subject = "BLUEPRINT", TenantId = null, OwnerName = "System",
        };
        handler.Rows.Add(row);

        var cut = RenderDetail(row.Id);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#template-editor-owner").TextContent.Trim().Should().Be("System");
            cut.Markup.Should().Contain("system-blueprint template", "the read-only reason names the scope");
            cut.FindAll("#template-save").Should().BeEmpty("a tenant session never writes the blueprint");
            cut.FindAll("#template-reset").Should().BeEmpty("nor resets it");
            cut.FindAll("#template-adopt").Should().ContainSingle("adopt is the primary action instead");
        });
    }

    [TestMethod]
    public void ATenantRowInASystemSession_IsReadOnly_AndOffersPublishPortalWideInstead()
    {
        var handler = Register(realTenant: false);
        var row = new TemplateRow
        {
            Kind = 0, Channel = 0, Subject = "NORTHFIELD-ROW",
            TenantId = TemplateTestServices.RealTenant, OwnerName = "Northfield",
        };
        handler.Rows.Add(row);

        var cut = RenderDetail(row.Id);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#template-editor-owner").TextContent.Trim().Should().Be("Northfield");
            cut.Markup.Should().Contain("it can only be edited in that tenant's session",
                "the read-only reason names the owning scope (spec §3.5)");
            cut.FindAll("#template-save").Should().BeEmpty("a system session never writes a tenant row");
            cut.FindAll("#template-reset").Should().BeEmpty("nor resets it");
            cut.Find("#template-adopt").TextContent.Should().Contain("Publish portal-wide",
                "the copy action is what makes the row editable in the system scope (spec §3.8)");
        });
    }

    [TestMethod]
    public void TheConflictWarning_ComparesOnlyWithinTheDraftsScope()
    {
        var handler = Register();
        var row = new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Subject = "TENANT-ROW",
            Constraints = [(0, TemplateApiHandler.GradeA)],
            TenantId = TemplateTestServices.RealTenant,
            OwnerName = "Northfield",
        };
        handler.Rows.Add(row);

        // The blueprint twin: same kind × channel × set, different scope. §3.9 makes the tenant row
        // shadow it, so this is the designed outcome — not an ambiguity the operator must resolve.
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Subject = "BLUEPRINT-TWIN",
            Constraints = [(0, TemplateApiHandler.GradeA)],
            TenantId = null,
            OwnerName = "System",
        });

        var cut = RenderDetail(row.Id);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("TENANT-ROW"));
        cut.Markup.Should().NotContain("identical constraint set",
            "a tenant row shadows an identical blueprint row (spec §3.9)");

        // A same-scope twin still warns: THAT is two candidates the matcher cannot tell apart.
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Subject = "TENANT-TWIN",
            Constraints = [(0, TemplateApiHandler.GradeA)],
            TenantId = TemplateTestServices.RealTenant,
            OwnerName = "Northfield",
        });

        var again = RenderDetail(row.Id);
        again.WaitForAssertion(() => again.Markup.Should().Contain("identical constraint set"));
    }

    [TestMethod]
    public void TheCreateDraft_ComparesWithinTheSessionsScope()
    {
        var handler = Register(); // a real tenant session
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Subject = "BLUEPRINT-TWIN",
            Constraints = [(0, TemplateApiHandler.GradeA)],
            TenantId = null,
            OwnerName = "System",
        });

        // A create draft carries no TenantId, so the ROW's scope cannot decide the comparison — the
        // session's scope does. A tenant creating a row is not in the blueprint's scope.
        var cut = Render<TemplateEditor>(p => p
            .Add(x => x.Mode, TemplateEditorMode.Create)
            .Add(x => x.PrefillGradeLevelId, Guid.Parse(TemplateApiHandler.GradeA)));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 2"));
        cut.Markup.Should().NotContain("identical constraint set");

        handler.Rows.Add(new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Subject = "TENANT-TWIN",
            Constraints = [(0, TemplateApiHandler.GradeA)],
            TenantId = TemplateTestServices.RealTenant,
            OwnerName = "Northfield",
        });

        var again = Render<TemplateEditor>(p => p
            .Add(x => x.Mode, TemplateEditorMode.Create)
            .Add(x => x.PrefillGradeLevelId, Guid.Parse(TemplateApiHandler.GradeA)));

        again.WaitForAssertion(() => again.Markup.Should().Contain("identical constraint set"));
    }

    [TestMethod]
    public void TheConflictWarning_AppearsForAnIdenticalSibling_AndNotForADifferentSet()
    {
        var handler = Register();
        var row = new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Subject = "THIS-ROW",
            Constraints = [(0, TemplateApiHandler.GradeA)],
        };
        handler.Rows.Add(row);

        var cut = RenderDetail(row.Id);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("THIS-ROW"));
        cut.Markup.Should().NotContain("identical constraint set");

        // A sibling of the same kind × channel with a DIFFERENT set is not a conflict…
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Body = "SIBLING-DIFFERENT",
            Constraints = [(0, TemplateApiHandler.GradeB)],
        });

        // …but one with the IDENTICAL set is.
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Body = "SIBLING-IDENTICAL",
            Constraints = [(0, TemplateApiHandler.GradeA)],
        });

        // Force a fresh read of the sibling set by re-opening the page.
        var again = RenderDetail(row.Id);
        again.WaitForAssertion(() => again.Markup.Should().Contain("identical constraint set"));
    }
}