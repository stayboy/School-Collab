namespace SchoolCollab.Assignments.Core.DTOs;

/// <summary>A single assignment row the lifecycle sweep is
/// considering for processing (WS-A2 / spec §3.5 step 8 / §7 Q6).
/// Carries the id + tenant id so the dispatched handler can be
/// wrapped in the candidate's explicit tenant context — see
/// <c>SchoolCollab.Assignments.Api.Services.ScheduledPublishSweeper</c>
/// and <c>ArchiveSweeper</c> for the dispatch shape.</summary>
public sealed record AssignmentSweepCandidate(Guid Id, Guid TenantId);
