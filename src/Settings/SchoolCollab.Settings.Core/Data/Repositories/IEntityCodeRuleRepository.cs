using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.Data.Repositories;

/// <summary>
/// Repository for <see cref="EntityCodeRule"/> aggregates (rule + owned segments).
/// </summary>
public interface IEntityCodeRuleRepository
{
    /// <summary>Gets the active rule for <paramref name="code"/> (with segments), or null.</summary>
    Task<EntityCodeRule?> GetActiveByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Gets a rule by id (with segments), ignoring soft-delete. Null if not found.</summary>
    Task<EntityCodeRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lists all non-deleted rules (with segments), ordered by code.</summary>
    Task<List<EntityCodeRule>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(EntityCodeRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists changes to <paramref name="rule"/>. Throws <see cref="ConcurrencyException"/>
    /// on an <c>xmin</c> optimistic-concurrency conflict so the caller can retry.
    /// </summary>
    Task UpdateAsync(EntityCodeRule rule, CancellationToken cancellationToken = default);
}