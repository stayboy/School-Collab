namespace SchoolCollab.Assignments.Core.DTOs;

/// <summary>
/// A candidate for the approval sweep (round <c>assignment-approval-workflow</c> / spec §3.4, §6).
/// Produced by the sanctioned cross-tenant read (<c>IgnoreQueryFilters(["Tenant"])</c>,
/// <c>AsNoTracking</c>) over assignments that are <b>both</b>
/// <see cref="Domain.ApprovalStatus.Pending"/> and <see cref="Domain.AssignmentStatus.Draft"/>, and
/// carrying everything the pure <c>ApprovalSweeper</c> needs to decide: the derived policy-scope
/// grade (so a per-grade <c>ApprovalWindowDays</c> override actually applies), the submission stamp
/// the window is measured from, and the due date that outranks the window.
/// </summary>
public sealed record AssignmentApprovalSweepCandidate(
    Guid AssignmentId,
    string Title,
    Guid TenantId,
    /// <summary>The DERIVED policy-scope grade — see
    /// <see cref="AssignmentReminderSweepCandidate.PolicyGradeId"/>. Null means the tenant-default
    /// policy; without it a per-grade approval window would silently never apply in the sweep.</summary>
    Guid? PolicyGradeId,
    /// <summary>The submission stamp (<c>Assignment.SubmittedAt</c>); null on a row that predates the
    /// column. A null stamp means no window can be measured, so the sweep falls through to the
    /// due-date rule.</summary>
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? DueDate);
