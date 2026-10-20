using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Round <c>assignment-approval-workflow</c> (WS2 + WS4) — two source shapes no behavioural test can
/// pin, both of which fail <b>silently</b> if they drift:
///
/// <list type="number">
/// <item><b>The effective assignment-policy resolver is registered by BOTH hosts and declared
/// once.</b> It was relocated from <c>Assignments.Api/Services</c> to
/// <c>Assignments.Core/Services</c> so the approval sweep in <c>Assignments.Worker</c> can resolve
/// the effective <c>ApprovalWindowDays</c> (the Worker references ServiceDefaults + Core only). A
/// host that loses its registration has no compile-time signal; the Worker would throw
/// <c>InvalidOperationException</c> inside a BackgroundService on the first sweep tick.</item>
/// <item><b>The pending-approval edit hold is on exactly the eight authoring write paths.</b> Plan
/// review iteration 2 found the hold's coverage — not its mechanism — is the risk: a missed command
/// is a silent hole an approver cannot see, and an <i>extra</i> one deadlocks a Pending row. The
/// per-row handler tests pin the behaviour; this pins the membership.</item>
/// </list>
///
/// Follows the <c>NotificationTemplateResolverWiringArchitectureTests</c> walk-up precedent.
/// </summary>
[TestClass]
public class AssignmentApprovalWorkflowArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private const string HeldPath = "assignment.EnsureNotAwaitingApproval();";

    // ── the resolver relocation ──────────────────────────────────────────

    [TestMethod]
    public void AssignmentsWorker_RegistersTheRelocatedAssignmentPolicyResolver_AfterAddAssignmentsCore()
    {
        var program = Read("src", "Assignments", "SchoolCollab.Assignments.Worker", "Program.cs");

        program.Should().Contain(
            "AddScoped<IAssignmentPolicyResolver, AssignmentPolicyResolver>();",
            "the approval sweep resolves the effective ApprovalWindowDays through it");

        var coreRegistration = program.IndexOf("AddAssignmentsCore(", StringComparison.Ordinal);
        var resolverRegistration = program.IndexOf(
            "AddScoped<IAssignmentPolicyResolver, AssignmentPolicyResolver>();", StringComparison.Ordinal);

        coreRegistration.Should().BeGreaterThanOrEqualTo(0);
        resolverRegistration.Should().BeGreaterThan(coreRegistration,
            "the resolver registration reads as part of the host's own block, after AddAssignmentsCore");
    }

    [TestMethod]
    public void AssignmentsApi_RegistersTheRelocatedAssignmentPolicyResolver_FromCore()
    {
        var program = Read("src", "Assignments", "SchoolCollab.Assignments.Api", "Program.cs");

        program.Should().Contain(
            "SchoolCollab.Assignments.Core.Services.IAssignmentPolicyResolver,",
            "the Api keeps serving the policy to every read and write seam");
        program.Should().Contain("SchoolCollab.Assignments.Core.Services.AssignmentPolicyResolver>();",
            "the registration must name the Core implementation, not a host-local copy");
    }

    [TestMethod]
    public void TheAssignmentPolicyResolver_IsDeclaredOnce_InAssignmentsCore()
    {
        Read("src", "Assignments", "SchoolCollab.Assignments.Core", "Services", "AssignmentPolicyResolver.cs")
            .Should().Contain("class AssignmentPolicyResolver",
                "one implementation means the fail-open contract is fixed and tested once");

        File.Exists(Path.Combine(
                RepoRoot, "src", "Assignments", "SchoolCollab.Assignments.Api", "Services", "AssignmentPolicyResolver.cs"))
            .Should().BeFalse("the Api copy was relocated, not duplicated");
    }

    // ── the hold's membership ────────────────────────────────────────────

    /// <summary>The eight HOLD rows of the round plan's WS2 table, by handler file.</summary>
    private static readonly string[] HeldHandlers =
    [
        "UpdateAssignmentCommand/UpdateAssignmentCommandHandler.cs",
        "QuestionsDraft/ConfirmQuestionsDraftCommandHandler.cs",
        "QuestionsDraft/StageQuestionsDraftCommandHandler.cs",
        "QuestionsDraft/DiscardQuestionsDraftCommandHandler.cs",
        "LinkAssignmentGroups/LinkAssignmentGroupsHandler.cs",
        "DeleteAssignmentCommand/DeleteAssignmentCommandHandler.cs",
        "RegenerateAttachmentExtraction/RegenerateAttachmentExtractionCommandHandler.cs",
        "SubmitAssignmentForApprovalCommand/SubmitAssignmentForApprovalCommandHandler.cs",
    ];

    /// <summary>The EXEMPT rows, by handler file — the hold must never appear in these.</summary>
    private static readonly string[] ExemptHandlers =
    [
        "ApproveAssignmentCommand/ApproveAssignmentCommandHandler.cs",
        "RejectAssignmentCommand/RejectAssignmentCommandHandler.cs",
        "StageAttachmentCommand/StageAttachmentCommandHandler.cs",
        "ReviewAssignmentCommand/ReviewAssignmentCommandHandler.cs",
        "DuplicateAssignmentCommand/DuplicateAssignmentCommandHandler.cs",
        "CreateAssignmentCommand/CreateAssignmentCommandHandler.cs",
        "PublishAssignmentCommand/PublishAssignmentCommandHandler.cs",
        "ScheduleAssignmentCommand/ScheduleAssignmentCommandHandler.cs",
        "UnpublishAssignmentCommand/UnpublishAssignmentCommandHandler.cs",
        "ArchiveAssignmentCommand/ArchiveAssignmentCommandHandler.cs",
        "CloseAssignmentCommand/CloseAssignmentCommandHandler.cs",
        "AutoReturnAssignmentCommand/AutoReturnAssignmentCommandHandler.cs",
        // Student / submission / sign-off paths — outside assignment authoring.
        "CreateStudentSubmission/CreateStudentSubmissionCommandHandler.cs",
        "SubmitAssignmentOnBehalf/SubmitAssignmentOnBehalfCommandHandler.cs",
        "EnableStudentSubmission/EnableStudentSubmissionCommandHandler.cs",
        "OverrideStudentSubmissionAttempts/OverrideStudentSubmissionAttemptsCommandHandler.cs",
        "ReviewSubmission/ReviewSubmissionCommandHandler.cs",
        "ReviewSubmissionGate/ReviewSubmissionGateCommandHandler.cs",
        "SignOff/FinalizeSignOffCommandHandler.cs",
        "SignOff/ReassignSignOffCommandHandler.cs",
        "SignOff/SignOffSubmissionCommandHandler.cs",
        "RecordModuleProgress/RecordModuleProgressCommandHandler.cs",
    ];

    [TestMethod]
    public void EveryHeldAuthoringHandler_CallsTheHold()
    {
        foreach (var handler in HeldHandlers)
        {
            ReadHandler(handler).Should().Contain(HeldPath,
                $"{handler} is a HOLD row of the WS2 table — without the call an author can change "
                + "content an approver is reviewing");
        }
    }

    [TestMethod]
    public void NoExemptHandler_CallsTheHold()
    {
        foreach (var handler in ExemptHandlers)
        {
            ReadHandler(handler).Should().NotContain(HeldPath,
                $"{handler} is an EXEMPT row — holding it would deadlock a Pending row (the release "
                + "paths), block a lifecycle op with its own guard, or reach outside assignment authoring");
        }
    }

    [TestMethod]
    public void TheHoldCall_SitsOnlyInTheEightHeldHandlers_AcrossTheWholeCommandsFolder()
    {
        var commandsRoot = Path.Combine(RepoRoot, "src", "Assignments", "SchoolCollab.Assignments.Core",
            "CQRS", "Assignments", "Commands");

        var carrying = Directory.EnumerateFiles(commandsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(HeldPath, StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .ToArray();

        carrying.Should().BeEquivalentTo(HeldHandlers.Select(Path.GetFileName),
            "the hold's coverage is the risk, in both directions — a missed path is a silent hole and "
            + "an extra one deadlocks the row");
    }

    private static string ReadHandler(string relative) =>
        Read("src", "Assignments", "SchoolCollab.Assignments.Core", "CQRS", "Assignments", "Commands", relative);

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
