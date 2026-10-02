using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Shared test double for <see cref="IAssignmentTargetResolver"/>
/// (documents/specs/assignment-authoring-compartments.md §7.2 TGT-8/TGT-10). Records the last
/// call so a test can assert the contract (the <c>allStudents</c> leg, the constraint kinds) and
/// can be made to throw so the fail-closed publish posture (D-6b) is exercised.
/// </summary>
internal sealed class FakeAssignmentTargetResolver : IAssignmentTargetResolver
{
    /// <summary>The resolved student ids. Empty is a legal answer — publish must refuse it (D-6c).</summary>
    public Guid[] StudentIds { get; set; } = [];

    /// <summary>When set, the resolve throws it — the resolver-outage path (TGT-10 / D-6b).</summary>
    public Exception? Throw { get; set; }

    public IReadOnlyList<TargetConstraint>? LastConstraints { get; private set; }

    public bool? LastIncludeAllStudents { get; private set; }

    public int CallCount { get; private set; }

    public Task<Guid[]> ResolveStudentIdsAsync(
        IReadOnlyList<TargetConstraint> targets,
        bool includeAllStudents,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastConstraints = targets;
        LastIncludeAllStudents = includeAllStudents;

        return Throw is null
            ? Task.FromResult(StudentIds)
            : Task.FromException<Guid[]>(Throw);
    }
}

/// <summary>
/// A permissive <c>IActivityGroupLookup</c> for the create/update handlers' D-8.1 group
/// resolution: it reports every requested id as active, so a test that does not care about the
/// archived-group rule still exercises the real call path.
/// </summary>
internal sealed class AcceptAllActivityGroupLookup : IActivityGroupLookup
{
    public Task<SchoolCollab.Assignments.Core.DTOs.ActivityGroupRefDto[]> GetByIdsAsync(
        IReadOnlyList<Guid> activityGroupIds, CancellationToken cancellationToken = default) =>
        Task.FromResult(activityGroupIds
            .Select(id => new SchoolCollab.Assignments.Core.DTOs.ActivityGroupRefDto(id, $"Group {id}", IsActive: true))
            .ToArray());

    public Task<Guid[]> GetActiveMemberIdsAsync(
        IReadOnlyList<Guid> activityGroupIds, CancellationToken cancellationToken = default) =>
        Task.FromResult(Array.Empty<Guid>());
}

/// <summary>
/// R2 (TGT-1) helpers that attach a valid authored target set to a test aggregate. The publish
/// path reads the target rows as the ONLY recipient source (D-1/D-6), so a fixture whose subject
/// is something else (the approval gate, deep links, the signature snapshot) still needs one row
/// before it can publish at all (TGT-13).
/// </summary>
internal static class AssignmentTargetTestExtensions
{
    public static Assignment WithAllStudentsTarget(this Assignment assignment)
    {
        assignment.SetTargets([(TargetKind.AllStudents, (Guid?)null)], assignment.TenantId);
        return assignment;
    }

    public static Assignment WithGradeTarget(this Assignment assignment, Guid gradeLevelId)
    {
        assignment.SetTargets([(TargetKind.GradeLevel, (Guid?)gradeLevelId)], assignment.TenantId);
        return assignment;
    }

    public static Assignment WithGroupTargets(this Assignment assignment, params Guid[] groupIds)
    {
        assignment.SetTargets(
            groupIds.Select(id => (Kind: TargetKind.ActivityGroup, RefId: (Guid?)id)).ToList(),
            assignment.TenantId);
        return assignment;
    }
}
