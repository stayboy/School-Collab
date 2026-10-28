using System;
using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the single-page student detail view
/// (<c>SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor</c>).
///
/// The student view is a single scrollable page (Details, Enrollment, Contacts, Guardians in the
/// main column; Activity groups, Subjects, Contact history in the rail) with no <c>FluentTabs</c>.
/// Adding tabs back, dropping a block, moving a per-row action up into a block header, or silently
/// losing a scoped-CSS class the markup consumes are all regressions that the build cannot see.
/// These checks catch that at test time.
///
/// Pattern: read the .razor source from disk and assert on its content. A bUnit render test would
/// be more "true", but Detail.razor depends on StudentsApiClient (a concrete class with 100+
/// methods, no IStudentsClient interface) and TenantGate, both of which are heavy to fake.
/// Source-level assertions are the right tool for the structural invariants the team actually cares
/// about — but they must assert on MARKUP, never on a comment: two of these tests once passed only
/// because a code comment happened to contain the phrase they searched for.
/// </summary>
[TestClass]
public class StudentDetailSectionsTests
{
    private static string ReadDetailSource()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Application",
            "Components", "Pages", "Students", "Detail.razor"));
        File.Exists(srcPath).Should().BeTrue(
            $"Detail.razor should exist at '{srcPath}' — check the path resolution");
        return File.ReadAllText(srcPath);
    }

    [TestMethod]
    public void Detail_Does_Not_Use_FluentTabs()
    {
        // Regression guard for the modernization's main user constraint:
        // "Do not use tabs to view guardians. Keep all parts on one page."
        var source = ReadDetailSource();
        source.Should().NotContain("<FluentTabs", "the student view is a single-page sectioned layout; do not reintroduce tabs");
        source.Should().NotContain("<FluentTab", "the student view is a single-page sectioned layout; do not reintroduce tabs");
    }

    [TestMethod]
    public void Detail_Renders_Each_Block_As_A_Panel_In_The_Documented_Order()
    {
        // documents/solution/panel-layout-pattern.md §2: every block on a create/view/edit page is a
        // PanelSection, main column first, rail LAST. The page's blocks, in reading order:
        //   main — Details, Enrollment, Contacts, Guardians
        //   rail — Activity groups, Subjects, Contact history
        // A block that reverts to a bare <h3> escapes the one-box rule (and its border, its count, its
        // header actions); a rail block moved into main silently changes how the page collapses at
        // 899px container width.
        var source = ReadDetailSource();

        source.Should().NotMatchRegex(@"<h3>\s*(Details|Enrollment|Contacts|Guardians|Activity groups|Subjects|Contact history)\s*</h3>",
            "blocks are PanelSections, never bare <h3> headings");

        string[] mainBlocks =
        [
            "student-view-details",
            "student-view-enrollment",
            "student-view-contacts",
            "student-view-guardians",
        ];
        string[] railBlocks =
        [
            "student-view-groups",
            "student-view-subjects",
            "student-view-contact-history",
        ];

        var mainIdx = source.IndexOf("class=\"region-grid__main\"", StringComparison.Ordinal);
        var railIdx = source.IndexOf("class=\"region-grid__rail\"", StringComparison.Ordinal);
        mainIdx.Should().BeGreaterThan(-1, "the page uses the shared two-region skeleton");
        railIdx.Should().BeGreaterThan(mainIdx, "the rail comes LAST (§2 — it is enrichment, not the point)");

        var previous = mainIdx;
        foreach (var id in mainBlocks)
        {
            var idx = source.IndexOf($"Id=\"{id}\"", StringComparison.Ordinal);
            idx.Should().BeGreaterThan(previous, $"block '{id}' must appear in the main column, in order");
            idx.Should().BeLessThan(railIdx, $"block '{id}' belongs to the main column, not the rail");
            previous = idx;
        }

        previous = railIdx;
        foreach (var id in railBlocks)
        {
            var idx = source.IndexOf($"Id=\"{id}\"", StringComparison.Ordinal);
            idx.Should().BeGreaterThan(previous, $"rail block '{id}' must appear after the rail opens, in order");
            previous = idx;
        }
    }

    [TestMethod]
    public void Enroll_Action_Lives_In_The_Enrollment_Panel_Header()
    {
        // Enroll is the Enrollment block's only action and it always applies — it needs no per-row
        // context, so it belongs in that block's header row (§2's "the first block's header carries
        // only its own controls"). One action ⇒ RowActionsMenu renders a labelled button, not a kebab.
        var source = ReadDetailSource();

        var panelIdx = source.IndexOf("Id=\"student-view-enrollment\"", StringComparison.Ordinal);
        panelIdx.Should().BeGreaterThan(-1, "the Enrollment block exists");

        var headerActionsIdx = source.IndexOf("<HeaderActions>", panelIdx, StringComparison.Ordinal);
        var bodyIdx = source.IndexOf("<ChildContent>", panelIdx, StringComparison.Ordinal);
        headerActionsIdx.Should().BeGreaterThan(panelIdx, "the Enrollment block has a header-actions slot");
        headerActionsIdx.Should().BeLessThan(bodyIdx, "the slot is in the HEADER, before the body");

        // The action list is bound there, and it is the one that calls OnEnrollAsync.
        source.Should().Contain("Actions=\"@EnrollmentPanelActions\"",
            "the header slot renders the Enrollment panel's action list");
        var actionsIdx = source.IndexOf("private IReadOnlyList<RowAction> EnrollmentPanelActions", StringComparison.Ordinal);
        actionsIdx.Should().BeGreaterThan(-1, "the Enrollment panel's action list is declared");
        source.Substring(actionsIdx).Should().Contain("OnEnrollAsync",
            "the panel action is Enroll");

        // …and Enroll is NOT in the page-level kebab: the page kebab carries page actions only
        // (edit / copy / back), so an author does not have to hunt two menus for the same verb.
        var pageActionsIdx = source.IndexOf("private IReadOnlyList<RowAction> PageActions", StringComparison.Ordinal);
        var pageActionsEnd = source.IndexOf("];", pageActionsIdx, StringComparison.Ordinal);
        source.Substring(pageActionsIdx, pageActionsEnd - pageActionsIdx)
            .Should().NotContain("OnEnrollAsync", "Enroll is the Enrollment block's action, not the page's");
    }

    [TestMethod]
    public void Transfer_And_Withdraw_Actions_Are_Per_Placement()
    {
        // Transfer and Withdraw are inherently per-enrollment operations — they change the row they
        // sit on. They are built by NodeActions(enrollment) for the ACTIVE node only, so a closed
        // placement renders no actions at all (RowActionsMenu renders nothing for an empty list).
        var source = ReadDetailSource();

        var nodeActionsIdx = source.IndexOf("private IReadOnlyList<RowAction> NodeActions(", StringComparison.Ordinal);
        nodeActionsIdx.Should().BeGreaterThan(-1, "the per-placement action builder exists");
        var nodeActionsEnd = source.IndexOf("\n\n", nodeActionsIdx, StringComparison.Ordinal);
        var nodeActions = source.Substring(nodeActionsIdx, nodeActionsEnd - nodeActionsIdx);

        nodeActions.Should().Contain("OnTransferAsync", "Transfer is a placement action");
        nodeActions.Should().Contain("OnWithdrawAsync", "Withdraw is a placement action");
        nodeActions.Should().Contain("IsActiveEnrollment(enrollment)",
            "only the current placement may be transferred or withdrawn — a closed one has no actions");

        // The list is consumed inside the timeline node, not in the panel header.
        var timelineIdx = source.IndexOf("<ul class=\"tl\">", StringComparison.Ordinal);
        timelineIdx.Should().BeGreaterThan(-1, "the placement history renders as a list");
        source.Substring(timelineIdx).Should().Contain("NodeActions(enrollment)",
            "each timeline node renders its own actions");

        // The panel-level list must not carry them: a transfer applies to ONE placement.
        var panelActionsIdx = source.IndexOf("private IReadOnlyList<RowAction> EnrollmentPanelActions", StringComparison.Ordinal);
        var panelActionsEnd = source.IndexOf("];", panelActionsIdx, StringComparison.Ordinal);
        var panelActions = source.Substring(panelActionsIdx, panelActionsEnd - panelActionsIdx);
        panelActions.Should().NotContain("OnTransferAsync");
        panelActions.Should().NotContain("OnWithdrawAsync");
    }

    [TestMethod]
    public void Detail_Title_Row_Uses_Enriched_Name_Format()
    {
        // The title is the user's primary identifier on the page.
        // Format: "FirstName LastName (Gender, Age)"
        var source = ReadDetailSource();
        source.Should().Contain("TitleLine", "Detail.razor uses a TitleLine computed property");
        // Sanity check the format pattern: a property for gender, age, full name.
        source.Should().Contain("AgeFromDob", "Detail.razor computes age from DOB for the title");
    }

    [TestMethod]
    public void Profile_Demographics_Are_Stacked_Top_Down()
    {
        // The user asked for a "top-down display" of demographics:
        // label on top, value below (a stat-card / metric-card pattern).
        // The shared <FieldDisplay> component now owns the styling, so we
        // assert on its scoped CSS: vertical orientation is column-direction,
        // label is a small uppercase hint, value is the prominent content.
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var cssPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "SchoolCollab.Admin.Shared", "Components", "FieldDisplay.razor.css"));
        File.Exists(cssPath).Should().BeTrue();
        var css = File.ReadAllText(cssPath);

        // .field-display-vertical must be a vertical flex container.
        var rowRule = ExtractRule(css, ".field-display-vertical");
        rowRule.Should().NotBeNull(".field-display-vertical is defined in FieldDisplay.razor.css");
        rowRule!.Should().Contain("flex-direction: column",
            ".field-display-vertical stacks label above value");
        rowRule!.Should().Contain("align-items: flex-start",
            ".field-display-vertical aligns its children to the start (left-aligned label/value stack)");

        // .field-display__label should be small, muted, uppercase (the "caption" style).
        var labelRule = ExtractRule(css, ".field-display__label");
        labelRule.Should().NotBeNull();
        labelRule!.Should().Contain("text-transform: uppercase",
            ".field-display__label is uppercase so it reads as a caption above the value");
        labelRule!.Should().Contain("var(--neutral-foreground-hint)",
            ".field-display__label uses the muted design-system color");

        // .field-display__value should be prominent (heavier than the label).
        var valueRule = ExtractRule(css, ".field-display__value");
        valueRule.Should().NotBeNull();
        valueRule!.Should().Contain("font-weight: 500",
            ".field-display__value is heavier than the label (the primary content)");
    }

    private static string? ExtractRule(string css, string selector)
    {
        // Match the first rule for the given selector (e.g. ".profile-row")
        // including the opening { and its body up to the matching closing }.
        //
        // We track a "inComment" state so selector occurrences inside
        // /* ... */ comments are not matched. We also require the selector
        // to be a whole identifier (not a prefix of ".profile-row-extended")
        // and to NOT be inside a string literal (class="...profile-row...").
        var inComment = false;
        for (var i = 0; i < css.Length; i++)
        {
            // Toggle comment state on /* and */
            if (inComment)
            {
                if (i + 1 < css.Length && css[i] == '*' && css[i + 1] == '/')
                {
                    inComment = false;
                    i++; // skip the /
                }
                continue;
            }
            if (i + 1 < css.Length && css[i] == '/' && css[i + 1] == '*')
            {
                inComment = true;
                i++; // skip the *
                continue;
            }

            // Try to match the selector at this position
            if (i + selector.Length > css.Length) break;
            if (string.CompareOrdinal(css, i, selector, 0, selector.Length) != 0) continue;

            // Boundary: previous char must not extend an identifier
            if (i > 0)
            {
                var prev = css[i - 1];
                if (char.IsLetterOrDigit(prev) || prev == '-' || prev == '_') continue;
            }
            // Boundary: next char must not extend an identifier
            if (i + selector.Length < css.Length)
            {
                var after = css[i + selector.Length];
                if (char.IsLetterOrDigit(after) || after == '-' || after == '_') continue;
            }
            // Inside a string? Walk back to find the last non-whitespace
            // char; if it's a quote, the selector is inside a string.
            var backIdx = i - 1;
            while (backIdx >= 0 && (css[backIdx] == ' ' || css[backIdx] == '\t'))
                backIdx--;
            if (backIdx >= 0 && (css[backIdx] == '"' || css[backIdx] == '\''))
                continue;

            // Find this rule's { and matching }.
            var open = css.IndexOf('{', i);
            if (open < 0) return null;
            var depth = 1;
            for (var j = open + 1; j < css.Length; j++)
            {
                if (css[j] == '{') depth++;
                else if (css[j] == '}')
                {
                    depth--;
                    if (depth == 0) return css.Substring(i, j - i + 1);
                }
            }
            return null;
        }
        return null;
    }

    [TestMethod]
    public void Guardians_Block_Renders_Cards_And_Wires_The_View_All_Contacts_Dialog()
    {
        // Each guardian is a CARD (monogram, name, relationship/emergency labels, contact chips, its own
        // kebab) — not a grid row with C1/C2/C3 columns. The contact-column grid was the reason this
        // block's content could not fit the rail, and it truncated a guardian's 4th contact out of
        // existence; the card shows the first three chips and hands the rest to the read-only dialog.
        var source = ReadDetailSource();

        source.Should().NotMatchRegex(@"<StudentGuardiansList\s+[A-Z]",
            "the guardian list is rendered inline as cards on this page — the grid subcomponent is retired here, "
            + "and this matcher is on the ELEMENT, not on the name: the name still appears in comments and in "
            + "<see cref=\"…\"/> references, which is how the first version of this assertion passed by accident");
        source.Should().NotContain("<GuardiansTab ",
            "the deprecated <GuardiansTab> must NOT be rendered");

        source.Should().Contain("<ul class=\"guardian-cards\">", "guardians render as cards");
        source.Should().Contain("@key=\"link.GuardianId\"", "the card loop is keyed by guardian id");
        source.Should().Contain("GuardianRowActions(link)", "each card carries its own kebab");

        // The 'View all' affordance opens the read-only contact dialog, and only when there ARE more.
        source.Should().Contain("link.HasMoreContacts",
            "the 'View all' affordance is gated by HasMoreContacts, never rendered as a dead link");
        source.Should().Contain("OnViewAllContactsAsync(link)",
            "the card's 'View all' chip calls the page handler");

        source.Should().Contain("OnManageGuardiansAsync",
            "the block's header action opens GuardianPickerDialog via ShowShellDialogAsync");
        source.Should().Contain("OnEditGuardianAsync",
            "the card kebab opens GuardianFormDialog in ForEdit mode");
        source.Should().Contain("ShowReadonlyDialogAsync<GuardianContactsDialog>(",
            "the handler opens GuardianContactsDialog via the DialogServiceExtensions.ShowReadonlyDialogAsync helper");
        source.Should().Contain("GuardianContactsDialog.GuardianIdKey",
            "GuardianId is passed under the dialog's published [Parameter] key");
        source.Should().Contain("GuardianContactsDialog.GuardianNameKey", "GuardianName is passed under its key");
        source.Should().Contain("GuardianContactsDialog.SubtitleKey", "Subtitle is passed under its key");
    }

    [TestMethod]
    public void EditGuardian_Draft_Preserves_TitleCodedValueId()
    {
        // The Edit guardian dialog's Title dropdown binds to
        // Model.TitleCodedValueId, which comes from the
        // GuardianAssignment draft built in OnEditGuardianAsync. The draft
        // MUST carry link.TitleCodedValueId (not null) so the guardian's
        // current title is pre-selected — a previous build passed null and
        // the dropdown showed nothing selected.
        var source = ReadDetailSource();
        source.Should().Contain("TitleCodedValueId: link.TitleCodedValueId",
            "the edit-guardian draft carries the guardian's TitleCodedValueId so the Title dropdown pre-selects it");
    }

    [TestMethod]
    public void The_View_Page_Is_Read_Only_It_Never_Embeds_A_Contact_Editor()
    {
        // The view page shows data; it does not edit it. Contact EDITING (add / verify / set primary /
        // remove) lives on the Edit form — EditContactEditorTests owns that side. This test replaces the
        // old "Profile card holds no Direct contact sub-section" check, whose subject (the FluentCard's
        // Direct contact block) no longer exists: the durable rule is that no editor appears HERE, not
        // that one particular card refuses it.
        var source = ReadDetailSource();

        source.Should().NotContain("<ContactsEditor",
            "the view page does NOT embed <ContactsEditor> — contact editing is on the Edit page");
        source.Should().NotContain("OwnerType=\"ContactOwnerType.Student\"",
            "no owner-scoped editor is rendered on the read-only view page");
        source.Should().NotContain("profile-section-sep",
            "the .profile-section-sep divider only existed to fence the old Direct contact block");
        source.Should().NotContain("section-header--sub",
            "the .section-header--sub h4 was the old Direct contact sub-header");

        // The student's own contacts are still SHOWN — a read-only chip list, with the edit path named
        // in the footer note instead of an inline editor.
        source.Should().Contain("Id=\"student-view-contacts\"", "the Contacts block exists");
        source.Should().Contain("class=\"contact-chips\"", "the contacts render as a read-only chip list");
    }

    [TestMethod]
    public void Contacts_Block_Lists_The_Students_Own_Contacts_Not_Guardian_Copies()
    {
        // Two different contact lists, deliberately adjacent and deliberately distinct:
        //   • Contacts block — the STUDENT's own rows (ContactOwnerType.Student), loaded from
        //     ListContactsAsync. Before this block existed the page rendered contact HISTORY without
        //     ever showing the current list: an audit trail of a list the reader could not see.
        //   • Guardian cards — each guardian's own contacts, on that guardian's card.
        // Conflating them (rendering guardian contacts in the Contacts block) would attribute a
        // guardian's mobile number to the student.
        var source = ReadDetailSource();

        source.Should().Contain("ListContactsAsync(ContactOwnerType.Student, Id",
            "the Contacts block loads the student-owner contact rows");

        var contactsIdx = source.IndexOf("Id=\"student-view-contacts\"", StringComparison.Ordinal);
        contactsIdx.Should().BeGreaterThan(-1, "the Contacts block exists");
        var guardiansIdx = source.IndexOf("Id=\"student-view-guardians\"", StringComparison.Ordinal);
        guardiansIdx.Should().BeGreaterThan(contactsIdx,
            "Contacts comes before Guardians: the student's own data precedes its people");

        // The block's own hint names the scope, so a reader never has to guess whose numbers these are.
        source.Substring(contactsIdx, guardiansIdx - contactsIdx)
            .Should().Contain("student's <strong>own</strong> contacts",
                "the block states whose contacts it lists");

        // Guardian contacts stay on the guardian cards (chips built from link.Contacts).
        source.Should().Contain("link.Contacts.Take(3)",
            "guardian contacts render on the guardian card, capped at three chips plus a 'View all' affordance");
    }

    [TestMethod]
    public void Detail_Razor_Css_Still_Defines_Every_Class_The_Markup_Consumes()
    {
        // Scoped CSS (.razor.css) silently DROPS a rule whose selector matches nothing at build time? No —
        // it silently keeps it, but the reverse failure is the one that bites: a rewrite of the component
        // drops a rule while the markup still references the class, and the page renders unstyled with no
        // warning. dialog-ui §3 calls this out. So: every class the markup uses must exist in the scoped
        // file, which is the durable half of the retired '.page-container/.title-row/.action-bar' check —
        // those three classes were deleted with the layout they styled, and asserting them would pin a
        // dead implementation rather than a live guarantee.
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var pagesDir = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Application", "Components", "Pages", "Students"));

        var source = File.ReadAllText(Path.Combine(pagesDir, "Detail.razor"));
        var css = File.ReadAllText(Path.Combine(pagesDir, "Detail.razor.css"));

        string[] pageOwnedClasses =
        [
            "student-monogram", "student-title", "student-meta", "student-meta__sep",
            "fact-grid",
            "placement", "placement__cell", "placement__label", "placement__value",
            "placement__gap", "placement__aside",
            "subrule", "tl", "tl__item", "tl__node", "tl__row", "tl__grade", "tl__actions", "tl__meta",
            "contact-chips", "contact-chip", "contact-chip__channel", "contact-chip__note", "contact-chip__more",
            "guardian-cards", "guardian-card", "guardian-card__body", "guardian-card__top",
            "guardian-card__name", "guardian-card__primary", "guardian-card__actions",
            "group-chip", "group-chip__category",
            "muted-note", "muted", "spinner-container",
            "student-contact-history", "student-contact-history__entry",
        ];

        foreach (var cls in pageOwnedClasses)
        {
            source.Should().Contain(cls, $"the markup must still use '{cls}' — this list drives the assertion below");
            css.Should().Contain($".{cls}", $"Detail.razor.css must define '.{cls}', which the markup consumes");
        }

        // The pill and the design tokens are SHARED, not page-scoped: they live in app.css so the next
        // surface gets them for free. Pinning the split here stops both copies appearing.
        css.Should().NotContain(".status-pill",
            ".status-pill is shared CSS in app.css (Admin.Shared/wwwroot) — never duplicate it per page");
        css.Should().NotContain("--status-success-bg:",
            "the state palette is DECLARED once, in app.css's :root and [data-theme=dark] blocks — consuming "
            + "the token here is the point, redefining it is the defect");
        source.Should().NotContain("color: #",
            "no literal colours in the markup; every colour comes from a token");
        css.Should().NotContain("#fafafa",
            "the old .student-contact-history__entry fell back to a literal #fafafa, which painted a white "
            + "card in dark mode — dead token fallbacks must not come back");
    }

    // --- Activity Groups section (spec: activity-group-enrollment.md) ---

    // AC-27 (FR-28): detail page shows an Activity groups block
    [TestMethod]
    public void Detail_Has_ActivityGroups_Section()
    {
        var source = ReadDetailSource();
        source.Should().Contain("Id=\"student-view-groups\"",
            "the student Detail page must have an Activity groups block");
        source.Should().Contain("Title=\"Activity groups\"",
            "the block's title is sentence case — Title Case is the page title's privilege, and the "
            + "section headings were renamed after 'Activity Groups' read as a title among labels");
    }

    // AC-27 (FR-28): the Activity groups block sits after Enrollment in reading order
    [TestMethod]
    public void Detail_ActivityGroups_Section_After_Enrollments()
    {
        var source = ReadDetailSource();
        var enrollmentsIdx = source.IndexOf("Id=\"student-view-enrollment\"", StringComparison.Ordinal);
        var groupsIdx = source.IndexOf("Id=\"student-view-groups\"", StringComparison.Ordinal);
        enrollmentsIdx.Should().BeGreaterThan(0, "the Enrollment block must exist");
        groupsIdx.Should().BeGreaterThan(enrollmentsIdx,
            "Activity groups must appear after Enrollment");
    }

    // AC-28 (FR-29): the join action is the block's header action
    [TestMethod]
    public void Detail_ActivityGroups_Has_JoinGroup_Action()
    {
        var source = ReadDetailSource();

        // Assert on the ACTION LIST, not on a phrase: this test used to pass because a code comment
        // happened to contain "Join Group", which meant the action could have been renamed or removed
        // and the assertion would still have gone green.
        var actionsIdx = source.IndexOf("private IReadOnlyList<RowAction> GroupPanelActions", StringComparison.Ordinal);
        actionsIdx.Should().BeGreaterThan(-1, "the Activity groups block declares its action list");
        var actionsEnd = source.IndexOf("];", actionsIdx, StringComparison.Ordinal);
        var actions = source.Substring(actionsIdx, actionsEnd - actionsIdx);
        actions.Should().Contain("OnJoinGroupAsync", "the block's action joins a group");
        actions.Should().Contain("Join activity group", "the action carries a visible label");

        source.Should().Contain("Actions=\"@GroupPanelActions\"",
            "the block's header renders that list");
    }

    // AC-30 (FR-31): each membership chip carries a remove (leave) affordance
    [TestMethod]
    public void Detail_ActivityGroups_Has_Leave_Action()
    {
        var source = ReadDetailSource();
        source.Should().Contain("OnRemove=\"() => OnLeaveGroupAsync(group.Id)\"",
            "each membership chip is removable — the chip's ✕ leaves the group");
        source.Should().Contain("<RailChip ",
            "memberships render as chips; a chip carries its own remove affordance, so the leave action "
            + "cannot drift away from the row it applies to");
    }

    // AC-31 (FR-32, NFR-11): section hidden when feature flag is OFF
    [TestMethod]
    public void Detail_ActivityGroups_Section_Gated_By_FeatureFlag()
    {
        var source = ReadDetailSource();
        source.Should().NotContain("FeatureFlagGate",
            "the Activity Groups section is NOT gated any more — FEATURE:EnableActivityGroups was retired "
            + "on 2026-10-26 and activity groups are an established feature, so re-wrapping this section in "
            + "a gate would silently hide it again");
        source.Should().Contain("Join activity group",
            "the block itself must survive the gate's removal — it is the student's group membership surface");
    }

    // ─── Contact history section (spec 2026-08-17 §6.3 / §7.3) ─────────
    // The append-only audit log of contact edits/deletes is surfaced as a
    // compact timeline below the Guardians section. Detail.razor is heavy
    // to fake (concrete StudentsApiClient + TenantGate), so these follow the
    // source-level pattern used by the other section tests.

    [TestMethod]
    public void Detail_Has_ContactHistory_Section_With_Count()
    {
        var source = ReadDetailSource();
        source.Should().Contain("Title=\"Contact history\"",
            "the student Detail page must render a Contact history block");
        // The count is the PanelSection's own, not a hand-rolled badge: one count mechanism for every
        // block (§2), which is why the old '<FluentBadge> next to the heading' assertion is gone — it
        // pinned the implementation and would have passed for any badge anywhere on the page.
        source.Should().Contain("Count=\"@_contactHistory.Length\"",
            "the Contact history header shows a live count of the loaded entries");
    }

    [TestMethod]
    public void Detail_ContactHistory_Section_Is_After_Guardians()
    {
        var source = ReadDetailSource();
        var guardiansIdx = source.IndexOf("Guardians", StringComparison.Ordinal);
        var historyIdx = source.IndexOf("Contact history", StringComparison.Ordinal);
        guardiansIdx.Should().BeGreaterThan(-1, "the Guardians section exists");
        historyIdx.Should().BeGreaterThan(guardiansIdx,
            "the Contact history section must appear below the Guardians section");
    }

    [TestMethod]
    public void Detail_ContactHistory_Loads_For_Student_Owner()
    {
        var source = ReadDetailSource();
        source.Should().Contain("ListContactAuditEntriesAsync",
            "the page calls the audit history endpoint");
        source.Should().Contain("ownerType: ContactOwnerType.Student",
            "history is loaded scoped to the student owner type");
        source.Should().Contain("ownerId: Id",
            "history is loaded for the current student id");
    }

    [TestMethod]
    public void Detail_ContactHistory_Has_Loading_Error_And_Empty_States()
    {
        var source = ReadDetailSource();
        source.Should().Contain("_contactHistoryLoading",
            "the history section has a loading spinner state");
        source.Should().Contain("_contactHistoryError",
            "the history section surfaces load errors");
        source.Should().Contain("No contact changes recorded yet.",
            "the history section has an empty state");
    }
}
