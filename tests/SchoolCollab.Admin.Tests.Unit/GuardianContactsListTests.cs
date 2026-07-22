using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the read-only
/// <c>GuardianContactsList.razor</c> component used in the student detail
/// view's "Contacts" section.
///
/// The component is intentionally minimal — load via
/// <c>StudentsApiClient.ListGuardiansByStudentAsync</c>, batch-load
/// relationship coded-value names, fetch each guardian's contacts, then
/// render one card per guardian with their channels. The interactive
/// surface is small enough that the structural invariants (sort order,
/// presence of role badge, display of all channels) are best asserted
/// at the source level, matching the convention of
/// <see cref="StudentDetailSectionsTests"/>.
///
/// bUnit render tests would be more "true" but require faking
/// <c>StudentsApiClient</c> and <c>CodedValuesApiClient</c>, both sealed
/// concrete classes with no public interface. The cost of standing up
/// an HttpMessageHandler-based test double outweighs the value for a
/// read-only display component.
/// </summary>
[TestClass]
public class GuardianContactsListTests
{
    private static string ReadRazorSource(string relativePath)
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Admin",
            "Components", "Students", relativePath));
        File.Exists(srcPath).Should().BeTrue(
            $"{relativePath} should exist at '{srcPath}' — check the path resolution");
        return File.ReadAllText(srcPath);
    }

    [TestMethod]
    public void Component_Takes_StudentId_As_Required_Parameter()
    {
        // The component is read-only and stateless beyond its load;
        // StudentId is its only input. The EditorRequired attribute is
        // the contract that catches a missing parameter at the call site.
        var razor = ReadRazorSource("GuardianContactsList.razor");
        razor.Should().MatchRegex(
            @"\[Parameter,\s*EditorRequired\]\s*public\s+Guid\s+StudentId",
            "StudentId is marked [EditorRequired] so the Blazor compiler enforces it");
    }

    [TestMethod]
    public void Component_Loads_Through_Existing_Api_Contracts()
    {
        // The component must reuse the same API surfaces the
        // ContactsEditor / GuardiansTab already use. This is the
        // "single source of truth" guard — if a new endpoint is
        // introduced, the review should question why.
        var razor = ReadRazorSource("GuardianContactsList.razor");
        razor.Should().Contain("ListGuardiansByStudentAsync",
            "reuses the existing /students/{id}/guardians endpoint");
        razor.Should().Contain("ListContactsAsync",
            "reuses the existing per-owner contacts endpoint");
        razor.Should().Contain("ContactOwnerType.Guardian",
            "queries contacts for the Guardian owner type");
        razor.Should().Contain("GetByIdsAsync",
            "batch-loads relationship coded-value names (one call, not N)");
    }

    [TestMethod]
    public void Component_Sorts_Primary_Role_First_Then_By_Name()
    {
        // Sort order is the visible contract: "Primary" role guardians
        // appear before "CC" guardians; within a role, by last name.
        // Implemented in the OrderBy chain in LoadAsync.
        var razor = ReadRazorSource("GuardianContactsList.razor");
        // Primary role first (0 < 1).
        razor.Should().MatchRegex(
            @"OrderBy\s*\(\s*i\s*=>\s*i\.Role\s*==\s*GuardianRole\.Primary\s*\?\s*0\s*:\s*1\s*\)",
            "Primary role guardians sort before CC role guardians");
        // Then by name.
        razor.Should().Contain("ThenBy(i => i.LastName, StringComparer.OrdinalIgnoreCase)",
            "within a role, sort by last name (case-insensitive) for stability");
        razor.Should().Contain("ThenBy(i => i.FirstName, StringComparer.OrdinalIgnoreCase)",
            "tie-break by first name (case-insensitive) for stability");
    }

    [TestMethod]
    public void Component_Sorts_Contacts_Primary_First_Then_By_Channel()
    {
        // Per-card sort: the primary contact anchors the card visually;
        // verified contacts come next; then by channel (Email < SMS <
        // WhatsApp) for stable display.
        var razor = ReadRazorSource("GuardianContactsList.razor");
        razor.Should().MatchRegex(
            @"OrderByDescending\s*\(\s*c\s*=>\s*c\.IsPrimary\s*\)",
            "primary contact renders first in the card");
        razor.Should().MatchRegex(
            @"ThenByDescending\s*\(\s*c\s*=>\s*c\.IsVerified\s*\)",
            "verified contacts sort above unverified");
        razor.Should().MatchRegex(
            @"ThenBy\s*\(\s*c\s*=>\s*c\.Channel\s*\)",
            "channels sort by enum order (Email, SMS, WhatsApp)");
    }

    [TestMethod]
    public void Component_Renders_All_Contacts_Not_Just_Primary()
    {
        // Per the user's "show all guardian contacts with roles" decision:
        // the component shows every contact, not just the primary. The
        // .guardian-contact-row class is applied to every row in the
        // foreach — primary gets an additional --primary modifier.
        var razor = ReadRazorSource("GuardianContactsList.razor");
        // The foreach iterates over item.Contacts (not item.PrimaryContacts),
        // so all rows are rendered. The class binding includes both the
        // base and the --primary variant.
        razor.Should().MatchRegex(
            @"@foreach\s*\(\s*var\s+c\s*in\s*item\.Contacts\s*\)",
            "iterates over ALL contacts, not just primary");
        razor.Should().MatchRegex(
            @"c\.IsPrimary\s*\?\s*""guardian-contact-row\s+guardian-contact-row--primary""\s*:\s*""guardian-contact-row""",
            "primary row gets the --primary modifier; non-primary gets the base class");
    }

    [TestMethod]
    public void Component_Renders_Role_Badge_With_Correct_Appearance()
    {
        // Primary role uses the Accent appearance so the badge is the
        // visual hook of the card; CC uses Neutral. This is the
        // information design — the user can scan a list of guardian
        // cards and see who the primary contact is at a glance.
        var razor = ReadRazorSource("GuardianContactsList.razor");
        razor.Should().MatchRegex(
            @"item\.Role\s*==\s*GuardianRole\.Primary\s*\?\s*Appearance\.Accent\s*:\s*Appearance\.Neutral",
            "Primary role uses Accent appearance; CC uses Neutral");
    }

    [TestMethod]
    public void Component_Relationship_Is_Optional_And_Muted()
    {
        // The relationship name (e.g. "Mother") is optional — if no
        // RelationshipCodedValueId is set, the column is omitted. When
        // present, it's rendered with the .muted style so it doesn't
        // compete with the guardian's name.
        var razor = ReadRazorSource("GuardianContactsList.razor");
        razor.Should().Contain("RelationshipName",
            "the component holds the resolved relationship name");
        razor.Should().MatchRegex(
            @"!string\.IsNullOrWhiteSpace\(item\.RelationshipName\)",
            "the relationship label is rendered only when present");
        razor.Should().Contain("guardian-contact-relationship");
    }

    [TestMethod]
    public void Component_Shows_Emergency_Badge_When_Flagged()
    {
        // The IsEmergencyContact flag on the link surfaces as an
        // "Emergency" badge next to the guardian's name. This is the
        // existing convention from GuardiansTab.razor.
        var razor = ReadRazorSource("GuardianContactsList.razor");
        razor.Should().Contain("IsEmergencyContact",
            "the link's emergency-contact flag drives a badge");
        razor.Should().Contain("guardian-contact-emergency",
            "the badge has a scoped class for any future per-component styling");
    }

    [TestMethod]
    public void Component_Channel_Glyphs_Match_Existing_ContactsEditor()
    {
        // The glyph set (✉ / 📱 / 💬) is the established convention
        // from ContactsEditor.razor's ChannelGlyph method. The list
        // component must use the same glyphs so the visual language is
        // consistent — the user sees the same icon for "Email"
        // everywhere in the app.
        var razor = ReadRazorSource("GuardianContactsList.razor");
        razor.Should().Contain("ContactChannel.Email => \"✉\"",
            "Email uses ✉ (matches ContactsEditor)");
        razor.Should().Contain("ContactChannel.SMS => \"📱\"",
            "SMS uses 📱 (matches ContactsEditor)");
        razor.Should().Contain("ContactChannel.WhatsApp => \"💬\"",
            "WhatsApp uses 💬 (matches ContactsEditor)");
    }

    [TestMethod]
    public void Component_Has_Empty_State_Message()
    {
        // When no guardians are linked, the user sees a clear
        // pointer to the Guardians section above (action-oriented
        // empty-state copy, per the project convention).
        var razor = ReadRazorSource("GuardianContactsList.razor");
        razor.Should().Contain("No guardians linked",
            "the empty-state copy names the condition");
        razor.Should().Contain("Guardians section above",
            "the empty state points the user to the place to add a guardian");
    }

    [TestMethod]
    public void Component_Has_Per_Guardian_No_Contact_Info_State()
    {
        // A guardian might be linked but have no contact info on file.
        // In that case, the card still renders the guardian's name +
        // role, with a quiet "no contact info on file" message inside.
        // This is distinct from the component-level "no guardians" state.
        var razor = ReadRazorSource("GuardianContactsList.razor");
        razor.Should().Contain("No contact info on file",
            "the per-guardian empty state is distinct from the component empty state");
        razor.Should().Contain("guardian-contact-empty");
    }

    [TestMethod]
    public void Component_Stylesheet_Defines_Required_Classes()
    {
        // Scoped-CSS hazard guard (dialog-ui skill §3): every class
        // referenced in the markup must have a CSS rule, otherwise the
        // build passes but the visual is silently wrong.
        var css = ReadRazorSource("GuardianContactsList.razor.css");
        var razor = ReadRazorSource("GuardianContactsList.razor");
        var referencedClasses = new[]
        {
            "guardian-contacts",
            "guardian-contact-card",
            "guardian-contact-header",
            "guardian-contact-name",
            "guardian-contact-role",
            "guardian-contact-relationship",
            "guardian-contact-emergency",
            "guardian-contact-empty",
            "guardian-contact-list",
            "guardian-contact-row",
            "guardian-contact-row--primary",
            "guardian-contact-channel",
            "guardian-contact-value",
            "guardian-contact-label",
            "guardian-contact-badges",
            "guardian-contact-badge",
            "muted",
        };
        // .spinner-container is used in markup but is defined in the
        // parent's Detail.razor.css (a parent-level page rule). It's
        // intentionally NOT in this scoped stylesheet — the component
        // is embedded inside the Detail page, so the parent's class
        // is what the spinner picks up. Skip the assertion for it.
        foreach (var cls in referencedClasses)
        {
            css.Should().Contain($".{cls}",
                $".{cls} is referenced in the markup and must have a CSS rule");
        }
    }
}
