namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// Issue #444 (layer L2b) — the six counts a student's assignment record implies, as this context's own
/// mirror of <c>SchoolCollab.Assignments.Contracts.StudentAssignmentStatsDto</c>. The mirror exists because
/// <c>Students.Core</c> may not reference another context's assembly (AGENTS.md § "Cross-context access"):
/// the hop is HTTP, so the type crosses as JSON and nothing else.
/// <para>
/// The member names are <b>identical</b> to the Assignments contract on purpose. <c>System.Text.Json</c>
/// binds by name, so a renamed member would deserialize to <c>0</c> instead of failing loudly — the worst
/// failure mode available. <c>StudentAssignmentStatsReaderHttpClientTests</c> pins all six values through
/// a real <c>ReadFromJsonAsync</c> round trip for exactly that reason. Every member is <see cref="int"/>:
/// no enum crosses this hop, so no converter is needed on either side.
/// </para>
/// <para>
/// The derived quantities are the caller's to compute, never fields on the wire (owner Q155): the funnel is
/// <c>Assigned − SentOut</c>, <c>SentOut − Submitted</c>, <c>Submitted − Marked</c> and <c>Marked</c>, and
/// the review axis is <c>Reviewed</c> over <c>ReviewRequired</c>. A <b>negative</b> difference means the
/// payload contradicts itself, and the caller renders "unavailable" rather than a clamped chart.
/// </para>
/// </summary>
public sealed record StudentAssignmentStats(
    /// <summary>Recipient-linked assignments of any status — a draft is counted here.</summary>
    int Assigned,
    /// <summary>Published, or scheduled with its publish time already passed.</summary>
    int SentOut,
    /// <summary>Sent, with a submission carrying a finalised timestamp.</summary>
    int Submitted,
    /// <summary>Sent, with that same submission finalised <b>and</b> graded.</summary>
    int Marked,
    /// <summary>Sent, on an assignment that mandates guardian review — the review axis's denominator.</summary>
    int ReviewRequired,
    /// <summary>Review-required, with the guardian-submission gate already reviewed.</summary>
    int Reviewed);
