using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Round <c>notification-templates-b1</c> — the source-shape guard for the tenant template
/// resolver, which no behavioural test can pin: the resolver lives in
/// <c>Assignments.Core</c> (one implementation, the <c>NotificationPolicyResolver</c>
/// relocation precedent) and BOTH hosts register it after <c>AddAssignmentsCore</c>, so the
/// E3 sweeps render an edited Reminder/Completion/Overdue template exactly as the publish path
/// renders Publish. A host that loses its registration silently falls back to
/// <c>NullNotificationTemplateResolver</c> — built-in defaults only, and no test of either
/// host's texts would notice.
///
/// Follows the <c>TeacherScopeWiringArchitectureTests</c> walk-up precedent.
/// </summary>
[TestClass]
public class NotificationTemplateResolverWiringArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string CoreRegistrationType =
        "SchoolCollab.Assignments.Core.Services.HttpNotificationTemplateResolver>();";

    private const string WorkerRegistration =
        "AddScoped<INotificationTemplateResolver, HttpNotificationTemplateResolver>();";

    [TestMethod]
    public void AssignmentsApi_RegistersTheHttpTemplateResolver()
    {
        var program = Read("src", "Assignments", "SchoolCollab.Assignments.Api", "Program.cs");

        program.Should().Contain("SchoolCollab.Assignments.Core.Services.INotificationTemplateResolver,",
            "the API host must resolve tenant templates over settings-api rather than the Core fallback");
        program.Should().Contain(CoreRegistrationType,
            "the registration must name the Core implementation, not a host-local copy");
    }

    [TestMethod]
    public void AssignmentsWorker_RegistersTheHttpTemplateResolver_AfterAddAssignmentsCore()
    {
        var program = Read("src", "Assignments", "SchoolCollab.Assignments.Worker", "Program.cs");

        program.Should().Contain(WorkerRegistration,
            "the E3 sweeps (Reminder/Completion/Overdue) render through the same resolver as publish");

        var coreRegistration = program.IndexOf("AddAssignmentsCore(", StringComparison.Ordinal);
        var resolverRegistration = program.IndexOf(WorkerRegistration, StringComparison.Ordinal);

        coreRegistration.Should().BeGreaterThanOrEqualTo(0);
        resolverRegistration.Should().BeGreaterThan(coreRegistration,
            "AddAssignmentsCore registers NullNotificationTemplateResolver — the HTTP resolver must be "
            + "registered AFTER it, since the last registration wins");
    }

    [TestMethod]
    public void TemplateResolver_IsDeclaredOnce_InAssignmentsCore()
    {
        Read("src", "Assignments", "SchoolCollab.Assignments.Core", "Services", "HttpNotificationTemplateResolver.cs")
            .Should().Contain("class HttpNotificationTemplateResolver",
                "one implementation means the fail-open filter is fixed and tested once");

        Directory.Exists(Path.Combine(RepoRoot, "src", "Assignments", "SchoolCollab.Assignments.Api", "Services"))
            .Should().BeTrue();

        Directory.EnumerateFiles(
                Path.Combine(RepoRoot, "src", "Assignments", "SchoolCollab.Assignments.Api", "Services"),
                "HttpNotificationTemplateResolver.cs")
            .Should().BeEmpty("the Api copy was relocated, not duplicated");

        Read("src", "Assignments", "SchoolCollab.Assignments.Core", "SchoolCollab.Assignments.Core.csproj")
            .Should().NotContain("SchoolCollab.Settings.Core",
                "Core holds no cross-context reference to Settings — the resolver deserializes into a "
                + "local wire mirror instead");
    }

    private static string Read(params string[] relative)
    {
        var path = Path.Combine([RepoRoot, .. relative]);
        return File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException($"Expected source file not found under the repo: {string.Join('/', relative)}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AppHost", "SchoolCollab.AppHost")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
