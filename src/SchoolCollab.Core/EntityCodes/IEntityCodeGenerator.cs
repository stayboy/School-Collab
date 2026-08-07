namespace SchoolCollab.Core.EntityCodes;

/// <summary>
/// Generates the next sequential entity code for a given rule (e.g.
/// <c>STUDENT_CODE</c>, <c>STAFF_CODE</c>, <c>ASSIGNMENT_CODE</c>). Implemented in
/// the Settings bounded context and consumed cross-bounded-context by the
/// Students and Assignments creation handlers (spec §4.6).
/// <para>
/// The contract lives in <c>SchoolCollab.Core</c> so bounded contexts can depend
/// on it without referencing Settings.Contracts.
/// </para>
/// </summary>
public interface IEntityCodeGenerator
{
    /// <summary>
    /// Generates and persists the next code for <paramref name="ruleCode"/>, advancing
    /// the rule's per-segment sequence state atomically. Throws
    /// <c>EntityCodeRuleNotFoundException</c> if no active rule exists.
    /// </summary>
    Task<string> GenerateAsync(string ruleCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="GenerateAsync"/>, but supplies a <paramref name="nameHint"/> that
    /// any <see cref="WordInitials"/> segments use to derive their initials (e.g.
    /// "computer science" → <c>CS</c>). Ignored when the rule has no such segment.
    /// </summary>
    Task<string> GenerateWithNameAsync(string ruleCode, string? nameHint, CancellationToken cancellationToken = default);
}