namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// One ward name resolution (WS-C1). Assignments.Core-owned shape — the
/// Students-boundary wire DTOs are mapped in the Assignments.Api
/// implementation so this module stays free of cross-context types.
/// </summary>
public sealed record StudentNameInfo(Guid StudentId, string DisplayName);

/// <summary>
/// One guardian link for a ward (WS-C1, spec §7 Q5). Determined from the
/// Students-boundary <c>StudentGuardian</c> links. Primary-priority signer
/// enforcement is recorded backlog — this flag is advisory display data in v1.
/// </summary>
public sealed record WardGuardianInfo(Guid GuardianId, string DisplayName, bool IsPrimary);

/// <summary>
/// Cross-bounded-context directory port (Assignments → Students) for the
/// guardian sign-off surfaces (assignment-request-implementation-details.md
/// §2 WS-C; spec §5 / §7 Q5). The interface lives in Assignments.Core; the
/// HTTP implementation (students-api named client) lives in Assignments.Api.
/// Mirrors <see cref="IActivityGroupLookup"/> /
/// <see cref="ISignatureDefaultResolver"/>.
///
/// <para>Failure posture: all lookups are best-effort — a fetch failure or a
/// 404 degrades to an absent result (null / empty list), never throws to the
/// caller. The callers degrade names to the raw id and treat an unverifiable
/// guardian as unauthorized (the sign handler must not fail open on
/// authorization — see the handler's <c>IsGuardianOfAsync</c> usage).</para>
/// </summary>
public interface IStudentDirectory
{
    /// <summary>Resolves a ward's display name ("First Last"). Null when the
    /// student cannot be resolved (404 or network failure).</summary>
    Task<StudentNameInfo?> GetStudentNameAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Resolves the ward's linked guardians (IDs + display names).
    /// Empty list when none are linked or on failure.</summary>
    Task<IReadOnlyList<WardGuardianInfo>> GetGuardiansAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="guardianId"/> is a linked guardian of
    /// the ward. Always false on failure (the sign/reassign handlers are
    /// fail-CLOSED on authorization — a directory outage blocks signing rather
    /// than letting an unlinked guardian through).</summary>
    Task<bool> IsGuardianOfAsync(Guid studentId, Guid guardianId, CancellationToken cancellationToken = default);
}