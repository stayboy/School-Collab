using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Regression tests for the student view redesign
/// (<c>documents/solution/student-view-redesign.md</c>): the shared skeleton, the state pill, the
/// subjects block and the contacts block.
/// <para>
/// Two kinds of assertion here, deliberately separated:
/// </para>
/// <list type="bullet">
/// <item>Source-level checks on <c>Detail.razor</c> — the same technique as
/// <see cref="StudentDetailSectionsTests"/>, and for the same reason (the page depends on a concrete
/// <c>StudentsApiClient</c> and <c>TenantGate</c>, which are heavy to fake).</item>
/// <item>Arithmetic on the colour TOKENS in <c>app.css</c> — a WCAG contrast computation, not a
/// recorded number. A recorded ratio goes stale the moment someone nudges a hex value; a computed one
/// fails on the nudged value, which is the whole point of a palette test.</item>
/// </list>
/// </summary>
[TestClass]
public class StudentViewRedesignTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "..", "..", "..", "..", ".."));

    private static string StudentsPagesDir => Path.Combine(
        RepoRoot, "src", "Students", "SchoolCollab.Students.Application", "Components", "Pages", "Students");

    private static string ReadDetailSource() => File.ReadAllText(Path.Combine(StudentsPagesDir, "Detail.razor"));

    private static string DetailCss => File.ReadAllText(Path.Combine(StudentsPagesDir, "Detail.razor.css"));

    private static string AppCss => File.ReadAllText(
        Path.Combine(RepoRoot, "src", "SchoolCollab.Admin.Shared", "wwwroot", "app.css"));

    // ── The skeleton ────────────────────────────────────────────────────────

    [TestMethod]
    public void The_View_Page_Wears_The_Shared_Authoring_Shell()
    {
        // documents/solution/panel-layout-pattern.md §2 + adr-authoring-surface-pattern.md decision 1:
        // an authoring surface wears the shared shell — crumb trail plus one page action header — and
        // never hand-rolls its own title row. The old page did exactly that: three <h3>s, a title row,
        // and a "Back to Students" button at the bottom. Edit.razor already speaks this shell; the view
        // page must not be the one surface that does not.
        var source = ReadDetailSource();

        source.Should().Contain("<AuthoringShell", "the view page wears the shared shell");
        source.Should().Contain("Crumbs=\"@ShellCrumbs\"", "the shell is given a crumb trail");
        source.Should().Contain("private IReadOnlyList<AuthoringCrumb> ShellCrumbs", "the trail is declared");

        source.Should().NotContain("PrimaryLabel=",
            "the view page commits NOTHING — a read-only surface renders no primary action, and an empty "
            + "or disabled one would be the shell's fake affordance");

        source.Should().Contain("TitleMenu", "the page's own kebab renders in the action header");
        source.Should().Contain("Actions=\"@PageActions\"", "the page kebab carries the page-level actions");
    }

    [TestMethod]
    public void Navigation_Lives_In_The_Crumb_And_The_Page_Kebab_Never_In_A_Footer_Button()
    {
        // Owner decision Q120: "Back to Students" sat BELOW the fold on a page whose only other exit was
        // the browser's back button. It now lives in two places a reader meets immediately — the crumb
        // ("Students" is a link) and the page kebab — and in none below the content.
        var source = ReadDetailSource();

        source.Should().NotContain("Back to Students</FluentButton>",
            "the footer back button is gone — navigation moved above the fold");
        source.Should().Contain("new AuthoringCrumb(\"Students\", \"/students\")",
            "the first crumb links back to the student list");
        source.Should().Contain("Label = \"Back to students\", Href = \"/students\"",
            "the page kebab repeats it, so the exit is reachable without scrolling up");
    }

    [TestMethod]
    public void Every_Page_Block_Is_A_PanelSection_With_A_Stable_Id()
    {
        // The ids are the test seam AND the documentation: student-view-<block>. A missing id means a
        // block lost its anchor, and every downstream assertion silently starts matching the wrong one.
        var source = ReadDetailSource();

        string[] blocks =
        [
            "student-view-details", "student-view-enrollment", "student-view-contacts",
            "student-view-guardians", "student-view-groups", "student-view-subjects",
            "student-view-contact-history",
        ];

        foreach (var id in blocks)
        {
            source.Should().Contain($"Id=\"{id}\"", $"block '{id}' exists");
        }

        // One box per block: main-column blocks are borderless because the region itself is the frame
        // (§2's lesson L2 — a region that draws a box double-frames every block inside it).
        source.Should().Contain("ShowBorder=\"false\"", "main-column blocks are borderless inside the region");
    }

    // ── The state pill ──────────────────────────────────────────────────────

    [TestMethod]
    public void Status_Pills_Are_Declared_Once_In_Shared_Css_With_All_Four_Variants()
    {
        // component-first-policy §2/§6: four uses on ONE surface are one call site, so this is CSS, not
        // a component. What it must not be is four copies of the same rule in a page stylesheet.
        var appCss = AppCss;

        appCss.Should().Contain(".status-pill {", "the pill is shared CSS in Admin.Shared/wwwroot/app.css");

        foreach (var variant in new[] { "success", "warning", "danger", "neutral" })
        {
            appCss.Should().Contain($".status-pill--{variant} {{", $"the '{variant}' variant is declared");
            appCss.Should().Contain($".status-pill--{variant} .status-pill__dot",
                $"the '{variant}' variant tints its dot");
        }

        DetailCss.Should().NotContain(".status-pill",
            "the pill lives in app.css only — a page-scoped copy is the drift this test exists to stop");
    }

    [TestMethod]
    public void The_Pill_Beats_The_Global_Span_Colour_Rule()
    {
        // app.css carries `p, span, label, small, strong, em, code, pre { color: var(--neutral-foreground-1) }`.
        // That rule is MORE specific than inheritance, so a pill's light-DOM label span takes #242424
        // instead of the pill's own foreground. That is precisely the defect the old Filled FluentBadge
        // shipped: #242424 on #0f6cbd, measured 2.88:1 — the same defect class the app bar carried until
        // 2026-10-24. The global rule stays (it is what keeps arbitrary markup readable), so the pill has
        // to win its own colour back explicitly. Asserting both halves keeps them coupled.
        var appCss = AppCss;

        appCss.Should().MatchRegex(@"p,\s*span,\s*label",
            "the global repaint rule still exists — if it were narrowed, the override below would be "
            + "redundant but harmless; if it is removed, both should go together");
        appCss.Should().Contain(".status-pill > span {", "the pill re-asserts its own foreground on its children");
        appCss.Should().Contain("color: inherit;", "…by inheriting from the pill, not by naming a second colour");
    }

    [TestMethod]
    public void Every_Status_Pill_Carries_A_Text_Label_And_A_Hidden_Dot()
    {
        // Colour never carries meaning alone (repo rule): a reader in greyscale, with a colour-vision
        // deficiency, or with a screen reader gets the LABEL. The dot is decoration and must not be
        // announced, or every status reads as "Enrolled bullet".
        var source = ReadDetailSource();

        var pills = Regex.Matches(source, @"class=""status-pill ").Count;
        pills.Should().BeGreaterThanOrEqualTo(4, "the page states its four statuses as pills");

        Regex.Matches(source, @"class=""status-pill__dot"" aria-hidden=""true""").Count
            .Should().Be(pills, "every pill carries exactly one dot, and every dot is aria-hidden");

        // No orphan dots: every dot sits inside a pill.
        Regex.Matches(source, @"class=""status-pill__dot""").Count
            .Should().Be(Regex.Matches(source, @"class=""status-pill__dot"" aria-hidden=""true""").Count,
                "a dot outside a pill, or one that forgot aria-hidden, is a rendering bug either way");
    }

    [TestMethod]
    public void The_Student_View_No_Longer_Asks_FluentUI_For_A_Badge_Colour_It_Does_Not_Paint()
    {
        // FluentUI 5.0.1's shipped rule for the success variant is misspelled (`fluent-badge[color=sucess]`),
        // so BadgeColor.Success silently paints nothing — 20 call sites across the repo were relying on
        // it. A state must not depend on that variant, here or on any surface this page owns.
        var source = ReadDetailSource();

        source.Should().NotContain("BadgeColor.Success",
            "BadgeColor.Success is a dead rule in FluentUI 5.0.1 — use the shared .status-pill");
        source.Should().NotContain("ChangeKindBadgeAppearance",
            "the change-kind badge became a pill; its Appearance helper returned Filled for both branches, "
            + "which is a helper that exists only to look like a decision");
    }

    [TestMethod]
    public void The_State_Palette_Is_Declared_In_Both_Themes_And_Meets_Wcag_Aa()
    {
        // A token with a light value and no dark value is a token that hides until the theme switches:
        // panel-layout-pattern.md §3 requires both. And a palette nobody measures drifts — the pill's own
        // reason to exist is contrast, so the contrast is computed, not trusted.
        var appCss = AppCss;
        var light = Scope(appCss, ":root {");
        var dark = Scope(appCss, "[data-theme=\"dark\"],");

        foreach (var variant in new[] { "success", "warning", "danger", "neutral" })
        {
            foreach (var suffix in new[] { "bg", "fg", "dot", "border" })
            {
                var token = $"--status-{variant}-{suffix}";

                Token(light, token).Should().NotBeNull($"'{token}' is declared for light mode");
                Token(dark, token).Should().NotBeNull($"'{token}' is declared for dark mode — a light-only token "
                    + "is a bug that only appears when the theme switches");
            }

            // Measured on the pair that actually carries the text: foreground against background.
            var lightRatio = ContrastRatio(Token(light, $"--status-{variant}-fg")!, Token(light, $"--status-{variant}-bg")!);
            lightRatio.Should().BeGreaterThanOrEqualTo(4.5,
                $"light '{variant}' pill text must meet WCAG AA for normal text (measured {lightRatio:F2}:1)");

            var darkRatio = ContrastRatio(Token(dark, $"--status-{variant}-fg")!, Token(dark, $"--status-{variant}-bg")!);
            darkRatio.Should().BeGreaterThanOrEqualTo(4.5,
                $"dark '{variant}' pill text must meet WCAG AA for normal text (measured {darkRatio:F2}:1)");
        }
    }

    [TestMethod]
    public void Every_Token_The_Page_Css_Consumes_Is_Declared_In_Both_Themes()
    {
        // The repo learned this the expensive way (2026-10-24): FluentUI 5.0.1 defines NO --neutral-* or
        // --accent-* token at all, so a `var(--neutral-stroke-rest)` in a component stylesheet makes its own
        // declaration invalid at computed-value time and the browser drops it SILENTLY — the rule looks
        // right in the file and applies only partially. The fallback in `var(--x, literal)` is what hides
        // it, which is why the old page's `.student-contact-history__entry` fell back to literal #fafafa
        // and painted a white card in dark mode.
        //
        // So: every token this page consumes must be DECLARED — in app.css :root (light) and in the dark
        // block — unless the page sets it inline at runtime.
        var css = DetailCss;
        var appCss = AppCss;
        var light = Scope(appCss, ":root {");
        var dark = Scope(appCss, "[data-theme=\"dark\"],");

        // Set by the component inline (a derived hue), so it has no stylesheet declaration to find.
        string[] setInline = ["--monogram-hue"];

        var consumed = Regex.Matches(css, @"var\((--[a-z0-9-]+)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Where(t => !setInline.Contains(t))
            .ToList();

        consumed.Should().NotBeEmpty("the page stylesheet consumes the shared palette");

        foreach (var token in consumed)
        {
            light.Should().Contain($"{token}:", $"app.css declares '{token}' for light mode — an undeclared token "
                + "silently deletes its own declaration and the fallback hides it");
            dark.Should().Contain($"{token}:", $"app.css re-declares '{token}' for dark mode — a light-only token "
                + "is a bug that only appears when the theme switches");
        }
    }
    // ── The subjects block (§1.5 of the redesign doc) ───────────────────────

    [TestMethod]
    public void Subjects_Block_Loads_The_Grade_Union_For_The_Active_Placement()
    {
        // The view page never called GET /students/{id}/subjects even though the endpoint and both band
        // variants already existed — the union was visible only on the Edit form. Reuse, not new work:
        // the same client method, the same two bands, the same RequireEnrollment split (FR-ER-1/FR-ER-3).
        var source = ReadDetailSource();

        source.Should().Contain("ListStudentSubjectsAsync(Id", "the block loads the union through the existing client method");
        source.Should().Contain("Id=\"student-view-subjects\"", "the block exists");
        source.Should().Contain("RequiresEnrollment", "the union is split on the RequiresEnrollment flag");

        // The split is the SAME split the Edit form makes, so the two surfaces cannot disagree about
        // which subjects a student is assumed to have.
        source.Should().Contain("s => s.RequiresEnrollment", "required subjects are RequiresEnrollment == true");
        source.Should().Contain("s => !s.RequiresEnrollment", "assumed subjects are RequiresEnrollment == false");
    }

    [TestMethod]
    public void Subjects_Block_Uses_The_Same_Two_Bands_As_The_Edit_Page()
    {
        var detail = ReadDetailSource();
        var edit = File.ReadAllText(Path.Combine(StudentsPagesDir, "Edit.razor"));

        foreach (var variant in new[] { "RailBandVariant.Required", "RailBandVariant.Assumed" })
        {
            detail.Should().Contain(variant, $"the view page renders the {variant} band");
            edit.Should().Contain(variant, $"the edit page renders the {variant} band");
        }

        // A band always keeps a text label — the tint is reinforcement, never the message.
        detail.Should().Contain("Label=\"Needs explicit enrolment\"");
        detail.Should().Contain("Label=\"Assumed\"");
    }

    [TestMethod]
    public void A_Missing_Required_Subject_Is_Blocked_On_The_View_Page_Not_Actionable()
    {
        // FR-ER-24 makes the EDIT page the enrolment surface. The view page shows the gap: the chip is
        // struck with the reason ("choose it on the Edit page") and carries no OnActivate. Rendering an
        // actionable chip here would duplicate the write path on a read-only page; rendering an
        // unstruck chip would hide the gap.
        var source = ReadDetailSource();

        var subjectsIdx = source.IndexOf("Id=\"student-view-subjects\"", StringComparison.Ordinal);
        var guardiansHistoryIdx = source.IndexOf("Id=\"student-view-contact-history\"", StringComparison.Ordinal);
        var block = source.Substring(subjectsIdx, guardiansHistoryIdx - subjectsIdx);

        block.Should().Contain("_subjectsError", "the block surfaces a load failure instead of hiding it");
        block.Should().Contain("IsEnrolled", "an enrolled required subject is a fact, not a gap");
        block.Should().Contain("Blocked=\"true\"", "an unenrolled required subject is struck");
        block.Should().Contain("Reason=", "…and says why, so the strike is never the only signal");
        block.Should().NotContain("OnActivate=",
            "enrolment is the Edit page's write path (FR-ER-24) — a read-only surface does not own it");

        _ = DetailCss; // the block's own styling lives in Detail.razor.css, asserted by the class-coverage test
    }

    // ── The contacts block (§1.6) ───────────────────────────────────────────

    [TestMethod]
    public void Contacts_Block_And_History_Are_Adjacent_Because_They_Answer_One_Question()
    {
        // The defect this block fixes: the page rendered contact HISTORY (a documented list of changes)
        // while never rendering the current list those changes described. Fixing the history's position
        // but not adding the list would leave the page explaining edits to data it never showed.
        var source = ReadDetailSource();

        var contactsIdx = source.IndexOf("Id=\"student-view-contacts\"", StringComparison.Ordinal);
        var guardiansIdx = source.IndexOf("Id=\"student-view-guardians\"", StringComparison.Ordinal);
        var historyIdx = source.IndexOf("Id=\"student-view-contact-history\"", StringComparison.Ordinal);

        contactsIdx.Should().BeGreaterThan(-1);
        guardiansIdx.Should().BeGreaterThan(contactsIdx, "Contacts precedes Guardians");
        historyIdx.Should().BeGreaterThan(guardiansIdx, "Contact history stays last, in the rail");

        // Both contact blocks state their OWNER, because the page has two contact lists that a reader
        // must not conflate: the student's and each guardian's.
        source.Substring(contactsIdx, guardiansIdx - contactsIdx)
            .Should().Contain("student's <strong>own</strong> contacts");
        source.Substring(historyIdx)
            .Should().Contain("student's own</strong> contacts",
                "the history is scoped to the student owner, and says so");
    }

    [TestMethod]
    public void The_Contact_Audit_Is_Loaded_For_The_Student_Owner_Not_For_Guardians()
    {
        // Guardian link edits are NOT in this audit: it is the student-owner trail. If the owner type
        // ever drifts to Guardian, the block silently shows an unrelated list.
        var source = ReadDetailSource();

        source.Should().Contain("ListContactAuditEntriesAsync");
        source.Should().Contain("ownerType: ContactOwnerType.Student");
        source.Should().Contain("ownerId: Id");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>The text of one CSS block, from its opening marker to the first closing brace.</summary>
    private static string Scope(string css, string marker)
    {
        var start = css.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"app.css declares the '{marker}' scope");
        var end = css.IndexOf('}', start);
        end.Should().BeGreaterThan(start);
        return css.Substring(start, end - start);
    }

    /// <summary>An <c>#rrggbb</c> custom-property value, or null when the scope does not declare it.</summary>
    private static string? Token(string scope, string name)
    {
        var match = Regex.Match(scope, $@"{Regex.Escape(name)}:\s*(#[0-9a-fA-F]{{6}})\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>WCAG 2.x contrast ratio between two sRGB hex colours.</summary>
    private static double ContrastRatio(string foreground, string background)
    {
        var a = RelativeLuminance(foreground);
        var b = RelativeLuminance(background);
        var lighter = Math.Max(a, b);
        var darker = Math.Min(a, b);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var value = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var channels = new[] { (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF };

        var linear = channels.Select(c =>
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }).ToArray();

        return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2];
    }
}
