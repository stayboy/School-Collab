using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the contacts API paths in
/// <c>StudentsApiClient.cs</c>.
///
/// The contacts API is registered as a sibling top-level group in
/// <c>SchoolCollab.Students.Api/StudentEndpoints.cs</c>:
///
///   <c>app.MapGroup("/contacts").MapContactRoutes().MapSubscriptionRoutes();</c>
///
/// The previous client prefixed these paths with <c>/students</c>
/// and the resulting 404 surfaced in the <c>&lt;ContactsEditor&gt;</c>
/// messagebar wherever the editor was used (Detail.razor view page,
/// GradeLevelWizard guardian step, etc.).
///
/// These tests assert the fix holds: the 9 contacts/subscription
/// paths use the root-level <c>/contacts</c> prefix and never the
/// broken <c>/students/contacts</c> prefix.
/// </summary>
[TestClass]
public class StudentsApiClientRoutesTests
{
    private static string ReadClientSource()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Application",
            "Services", "StudentsApiClient.cs"));
        File.Exists(srcPath).Should().BeTrue(
            $"StudentsApiClient.cs should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
    }

    /// <summary>
    /// Extract every <c>"/...contacts..."</c> string literal from the file so
    /// we can assert path-by-path. The API client uses both interpolated
    /// (<c>$"..."</c>) and plain (<c>"..."</c>) string forms. The 9
    /// expected endpoints are documented in <c>ContactRoutes.cs</c> +
    /// <c>SubscriptionRoutes.cs</c>.
    /// </summary>
    private static List<string> ExtractContactPaths(string source)
    {
        // Combined pattern: optional `$` + double-quote + capture group +
        // closing double-quote. Captures the path content.
        var rx = new System.Text.RegularExpressions.Regex(
            @"\$?""(/[^""]*contacts[^""]*)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var all = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in rx.Matches(source))
        {
            var p = m.Groups[1].Value;
            // Skip C# type/parameter names that happen to start with a
            // path-like string (e.g. "IContactsClient", "AddContactRequest",
            // "SubscriptionRequest") — these never start with "/contacts".
            // Also skip XML doc comment artifacts (cref attributes, see tags,
            // and the "/>" closing of a cref/see tag, which the greedy regex
            // can latch onto and span a huge non-path region).
            if (p.Contains("AddContactRequest") || p.Contains("SubscriptionRequest")) continue;
            if (p.Contains("cref=") || p.Contains("see cref=") || p.StartsWith(">") || p.StartsWith("/>")) continue;
            all.Add(p);
        }
        return all.Distinct().ToList();
    }

    [TestMethod]
    public void Contacts_Paths_Use_Root_Level_Prefix_Not_Students_Subresource()
    {
        // The fix: drop the bogus `/students` prefix from every
        // contacts / subscription path. The previous paths
        // (/students/contacts, /students/contacts/{id}, etc.) 404'd
        // because the API group is /contacts, NOT /students/contacts.
        var source = ReadClientSource();
        var paths = ExtractContactPaths(source);
        paths.Should().NotBeEmpty(
            "the client must call at least one contacts endpoint");

        paths.Should().NotContain(p => p.StartsWith("/students/contacts", StringComparison.Ordinal),
            "no contact path may use the dead /students/contacts prefix — the API is at /contacts");

        // Every extracted path must start with /contacts (the root-level group).
        paths.Should().AllSatisfy(p => p.Should().StartWith("/contacts",
            $"the path '{p}' must be a root-level /contacts endpoint (the API group is /contacts, not /students/contacts)"));
    }

    [TestMethod]
    public void Contacts_Paths_Cover_All_Endpoints()
    {
        // The contacts/subscription endpoints documented in
        // ContactRoutes.cs + SubscriptionRoutes.cs. Asserting by
        // presence of each path guards against a missing one being
        // silently dropped during refactors.
        var source = ReadClientSource();
        var paths = ExtractContactPaths(source);
        var joined = string.Join("\n", paths);

        joined.Should().Contain("/contacts?ownerType={ownerType}&ownerId={ownerId}",
            "ListContactsAsync must GET /contacts?ownerType=...&ownerId=...");
        paths.Should().Contain("/contacts",
            "AddContactAsync must POST to /contacts (exact path, no sub-resource)");
        joined.Should().Contain("/contacts/{id}",
            "UpdateContactAsync must PUT /contacts/{id} AND DeleteContactAsync must DELETE /contacts/{id}");
        joined.Should().Contain("/contacts/{id}/verify",
            "VerifyContactAsync must POST /contacts/{id}/verify");
        joined.Should().Contain("/contacts/{id}/order",
            "SetContactOrderAsync must POST /contacts/{id}/order");
        joined.Should().Contain("/contacts/reorder",
            "ReorderContactsAsync must POST /contacts/reorder");
        joined.Should().Contain("/contacts/subscribed?ownerType={ownerType}&scope={scope}",
            "ListSubscribedContactsAsync must GET /contacts/subscribed?ownerType=...&scope=... when scope is provided");
        joined.Should().Contain("/contacts/subscribed?ownerType={ownerType}{(ownerId",
            "ListSubscribedContactsAsync must GET /contacts/subscribed?ownerType=... when no scope is provided");
        joined.Should().Contain("/contacts/{contactId}/subscribe",
            "SubscribeAsync must POST /contacts/{contactId}/subscribe");
        joined.Should().Contain("/contacts/{contactId}/unsubscribe",
            "UnsubscribeAsync must POST /contacts/{contactId}/unsubscribe");
    }

    [TestMethod]
    public void Contacts_Paths_Are_Documented_In_Source_Comment()
    {
        // The fix added an explanatory comment in StudentsApiClient.cs
        // pointing at the real registration in
        // SchoolCollab.Students.Api/StudentEndpoints.cs. Guarding
        // the comment prevents a future refactor from accidentally
        // re-introducing the bogus /students prefix and losing the
        // breadcrumb.
        var source = ReadClientSource();
        source.Should().Contain("app.MapGroup(\"/contacts\")",
            "the source must document the real API group registration");
        source.Should().Contain("StudentEndpoints.cs",
            "the source must point readers to the file that owns the route registration");
        source.Should().Contain("NOT a /students/contacts",
            "the source must call out the asymmetry between the previous (broken) path and the real one");
    }
}
