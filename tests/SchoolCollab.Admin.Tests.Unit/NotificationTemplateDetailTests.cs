using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The template detail page (redesign spec §3.5–§3.6, §3.12; naming spec D3/D11), tested through the
/// route host: the two dropdowns are locked indicators whose "fixed per template" rule is the row's help
/// tooltip, one Save replaces the row by id after the operator states a reason in the shared dialog
/// (dismissing it abandons the write and keeps the draft), Reset opens that same dialog and is the only
/// destructive confirmation, the audit grid is narrowed to THIS template, and the conflict warning
/// appears only for an identical sibling set.
/// <para>
/// Dialog invocation is exercised through the mocked <see cref="TemplateDialogs"/> fake
/// (<see cref="FluentServiceMocks"/> records the v5 mocking trap); the constraint-edit dialog keeps its
/// own direct suite (<see cref="ConstraintEditDialogTests"/>).
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

    /// <summary>Registers the dialog fake after the page's own services (register before the first render).</summary>
    private TemplateDialogs RegisterDialogs() => TemplateDialogs.Register(this);

    /// <summary>
    /// Renders the SHARED EDITOR in Edit mode — the detail page is a six-line route host, and a path
    /// parameter (`{Id:guid}`) is supplied by the Router, which a bare bUnit tree does not have (the route
    /// value, unlike a `[SupplyParameterFromQuery]`, is not resolved from the navigation state).
    /// </summary>
    private IRenderedComponent<TemplateEditor> RenderDetail(Guid id) =>
        Render<TemplateEditor>(p => p
            .Add(x => x.Mode, TemplateEditorMode.Edit)
            .Add(x => x.Id, id));

    private static IElement FormRowLabelled(IRenderedComponent<TemplateEditor> cut, string label) =>
        cut.FindAll(".form-row").Single(r =>
            r.QuerySelector(".form-row-label")!.TextContent.TrimStart().StartsWith(label, StringComparison.Ordinal));

    private static async Task TypeBodyAsync(IRenderedComponent<TemplateEditor> cut, string body)
    {
        var control = cut.FindComponents<FluentTextArea>().Single(a => a.Instance.Id == "template-body-text");
        await cut.InvokeAsync(() => control.Instance.ValueChanged.InvokeAsync(body));
    }

    [TestMethod]
    public void TypeAndContactMode_AreLockedIndicators_WithTheirReasonOnTheRowsHelpTooltip()
    {
        // Arrange
        var handler = Register();
        RegisterDialogs();
        var row = new TemplateRow { Kind = 0, Channel = 0, Title = "LOCKED-ROW" };
        handler.Rows.Add(row);

        // Act
        var cut = RenderDetail(row.Id);

        // Assert
        cut.WaitForAssertion(() =>
        {
            cut.FindComponents<DropdownComponent<TemplateEditor.KindOption, NotificationTemplateKind>>()
                .Single().Instance.Disabled.Should().BeTrue();
            cut.FindComponents<DropdownComponent<TemplateEditor.ChannelOption, NotificationChannel>>()
                .Single().Instance.Disabled.Should().BeTrue();

            // D9: the rule left the page as a paragraph and became the row label's help hint, so the
            // page stays compact while the constraint stays discoverable.
            var typeRow = FormRowLabelled(cut, "Template Type");
            typeRow.QuerySelector("[title*='fixed per template']").Should().NotBeNull(
                "the 'type and contact mode are fixed per template' rule is the row's help tooltip");
        });
    }

    [TestMethod]
    public async Task Save_ReplacesTheRowById_WithTheStagedDraft_AndTheReasonTypedIntoTheDialog()
    {
        // Arrange
        var handler = Register();
        var dialogs = RegisterDialogs();
        var row = new TemplateRow
        {
            Kind = 0,
            Channel = 1,
            Title = "Grade 8 Science reminder",
            Body = "OLD-BODY",
            Constraints = [(0, TemplateApiHandler.GradeA)],
        };
        handler.Rows.Add(row);

        var cut = RenderDetail(row.Id);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("OLD-BODY"));

        await TypeBodyAsync(cut, "NEW-BODY");
        dialogs.ReasonAnswer = "Wording fix after the science review";

        // Act
        cut.Find("#template-save").Click();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var put = handler.Calls.Single(c => c.Method == "PUT");
            put.Url.Should().Contain($"/notification-templates/{row.Id:D}");
            put.Body.Should().Contain("NEW-BODY");
            put.Body.Should().Contain(TemplateApiHandler.GradeA);
            put.Body.Should().Contain("\"reason\":\"Wording fix after the science review\"",
                "the edit's audit reason is the dialog's answer (D11)");
        }, TimeSpan.FromSeconds(5));

        dialogs.ReasonOpenCount.Should().Be(1, "the reason dialog is the save's only interruption (D11)");
        dialogs.LastReasonTitle.Should().Contain("Grade 8 Science reminder",
            "the dialog names the row it is about to change (D3)");
    }

    [TestMethod]
    public async Task Save_WithARenamedTitle_SendsTheNewTitle_SoARenameIsAttributable()
    {
        // Arrange
        var handler = Register();
        var dialogs = RegisterDialogs();
        var row = new TemplateRow { Kind = 0, Channel = 1, Title = "Old name", Body = "BODY" };
        handler.Rows.Add(row);

        var cut = RenderDetail(row.Id);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Old name"));

        var title = cut.FindComponents<FluentTextInput>().Single(i => i.Instance.Id == "template-title");
        await cut.InvokeAsync(() => title.Instance.ValueChanged.InvokeAsync("Grade 8 Science reminder"));
        dialogs.ReasonAnswer = "Fixing a naming mistake";

        // Act
        cut.Find("#template-save").Click();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var put = handler.Calls.Single(c => c.Method == "PUT");
            put.Body.Should().Contain("\"title\":\"Grade 8 Science reminder\"",
                "the title is editable on an existing template (D16)");
            put.Body.Should().Contain("\"reason\":\"Fixing a naming mistake\"");
        }, TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task Save_WithADismissedReasonDialog_PerformsNoWrite_AndLeavesTheDraft()
    {
        // Arrange
        var handler = Register();
        var dialogs = RegisterDialogs();
        var row = new TemplateRow { Kind = 0, Channel = 1, Title = "Grade 8 Science reminder", Body = "OLD" };
        handler.Rows.Add(row);

        var cut = RenderDetail(row.Id);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("OLD"));

        await TypeBodyAsync(cut, "STAGED-BUT-NOT-SAVED");

        // Act — cancelling the dialog abandons the save (D11).
        dialogs.ReasonAnswer = null;
        cut.Find("#template-save").Click();

        // Assert
        cut.WaitForAssertion(() => dialogs.ReasonOpenCount.Should().Be(1,
            "the dialog opens before the write, so dismissing it is what stops the write"));
        handler.Calls.Should().NotContain(c => c.Method == "PUT", "no reason, no write");

        cut.FindComponents<FluentTextArea>().Single(a => a.Instance.Id == "template-body-text")
            .Instance.Value.Should().Be("STAGED-BUT-NOT-SAVED",
                "the operator's draft survives a cancelled save");
        cut.FindAll("#template-editor-error").Should().BeEmpty(
            "a cancelled dialog is not a failure — it produces no error to explain");
    }

    [TestMethod]
    public void Reset_DismissingTheReasonDialog_IsNotAWrite()
    {
        // Arrange
        var handler = Register();
        var dialogs = RegisterDialogs();
        var row = new TemplateRow { Kind = 0, Channel = 0, Title = "RESET-ME" };
        handler.Rows.Add(row);

        var cut = RenderDetail(row.Id);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("RESET-ME"));

        // Act
        dialogs.ReasonAnswer = null;
        cut.Find("#template-reset").Click();

        // Assert
        cut.WaitForAssertion(() => dialogs.ReasonOpenCount.Should().Be(1));
        handler.Calls.Should().NotContain(c => c.Method == "DELETE", "a dismissed reset writes nothing");
    }

    [TestMethod]
    public void Reset_WithAReason_UsesTheSharedReasonDialog_AndDeletesWithTheReason()
    {
        // Arrange
        var handler = Register();
        var dialogs = RegisterDialogs();
        var row = new TemplateRow { Kind = 0, Channel = 0, Title = "RESET-ME" };
        handler.Rows.Add(row);

        var cut = RenderDetail(row.Id);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("RESET-ME"));

        // Act
        dialogs.ReasonAnswer = "Superseded by the new reminder";
        cut.Find("#template-reset").Click();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var delete = handler.Calls.Single(c => c.Method == "DELETE");
            delete.Url.Should().Contain($"/notification-templates/{row.Id:D}");
            delete.Url.Should().Contain("reason=Superseded%20by%20the%20new%20reminder",
                "a reset is audited with the operator's own words");
        });
        dialogs.LastReasonTitle.Should().Contain("RESET-ME", "the reset prompt names the row by its title");
        dialogs.ReasonOpenCount.Should().Be(1,
            "reset opens the SAME shared reason dialog as a save — one reason experience, not two (D11)");
    }

    [TestMethod]
    public void TheAuditTrail_IsNarrowedToThisTemplate()
    {
        var handler = Register();
        RegisterDialogs();
        var row = new TemplateRow { Kind = 0, Channel = 0, Title = "AUDITED" };
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
        RegisterDialogs();
        var row = new TemplateRow { Kind = 0, Channel = 0, Title = "MINE" };
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
        RegisterDialogs();
        var row = new TemplateRow
        {
            Kind = 0, Channel = 0, Title = "BLUEPRINT", TenantId = null, OwnerName = "System",
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
        RegisterDialogs();
        var row = new TemplateRow
        {
            Kind = 0, Channel = 0, Title = "NORTHFIELD-ROW",
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
    public void TheShell_RendersTheBreadcrumbTrail_NamingTheTemplateByItsTitle()
    {
        // Arrange
        var handler = Register();
        RegisterDialogs();
        var row = new TemplateRow
        {
            Kind = 0, Channel = 0, Title = "Grade 8 Science reminder", Subject = "A subject that is not a name",
        };
        handler.Rows.Add(row);

        // Act
        var cut = RenderDetail(row.Id);

        // Assert
        cut.WaitForAssertion(() =>
        {
            cut.Find("#authoring-breadcrumb").TextContent.Should().Contain("Notification templates",
                "the shared AuthoringShell supplies the trail (ADR decision 1)");
            cut.Find("#authoring-breadcrumb .authoring-crumb-current").TextContent.Trim()
                .Should().Be("Grade 8 Science reminder",
                    "the row's identity is its title, never its subject line (naming spec D3)");
        });
    }

    [TestMethod]
    public void TheConflictWarning_ComparesOnlyWithinTheDraftsScope()
    {
        // Arrange
        var handler = Register();
        RegisterDialogs();
        var row = new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Title = "TENANT-ROW",
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
            Title = "BLUEPRINT-TWIN",
            Constraints = [(0, TemplateApiHandler.GradeA)],
            TenantId = null,
            OwnerName = "System",
        });

        var cut = RenderDetail(row.Id);

        // Act / Assert — the blueprint twin is not a conflict…
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("TENANT-ROW"));
        cut.Markup.Should().NotContain("identical constraint set",
            "a tenant row shadows an identical blueprint row (spec §3.9)");

        // A same-scope twin still warns: THAT is two candidates the matcher cannot tell apart, and the
        // warning names the other row by its title so the operator can go and find it (D3).
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Title = "TENANT-TWIN",
            Constraints = [(0, TemplateApiHandler.GradeA)],
            TenantId = TemplateTestServices.RealTenant,
            OwnerName = "Northfield",
        });

        var again = RenderDetail(row.Id);

        // …and the same-scope twin is, named by its title (D3).
        again.WaitForAssertion(() =>
        {
            again.Markup.Should().Contain("identical constraint set");
            again.Find("#template-conflict-warning").TextContent.Should().Contain("TENANT-TWIN",
                "the warning names the other template by its title (D3)");
        });
    }

    [TestMethod]
    public void TheCreateDraft_ComparesWithinTheSessionsScope()
    {
        var handler = Register(); // a real tenant session
        RegisterDialogs();
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Title = "BLUEPRINT-TWIN",
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
            Title = "TENANT-TWIN",
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
        RegisterDialogs();
        var row = new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Title = "THIS-ROW",
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
            Title = "SIBLING-DIFFERENT",
            Constraints = [(0, TemplateApiHandler.GradeB)],
        });

        // …but one with the IDENTICAL set is.
        handler.Rows.Add(new TemplateRow
        {
            Kind = 0,
            Channel = 0,
            Title = "SIBLING-IDENTICAL",
            Constraints = [(0, TemplateApiHandler.GradeA)],
        });

        // Force a fresh read of the sibling set by re-opening the page.
        var again = RenderDetail(row.Id);
        again.WaitForAssertion(() => again.Markup.Should().Contain("identical constraint set"));
    }
}
