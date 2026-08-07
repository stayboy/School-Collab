using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Stores <b>how</b> an entity code is generated for a given entity type
/// (e.g. student numbers, staff numbers, assignment numbers). Lives alongside
/// the existing <see cref="CodedValue"/> aggregate but is a separate concern:
/// CodedValues are reference data; generation rules are operational configuration
/// (spec §3.1).
/// <para>
/// Hybrid tenant entity — shared-blueprint rules (<c>TenantId = null</c>) are
/// CSV-seeded and visible to all tenants; tenant-owned rules are isolated. The
/// template is an ordered collection of <see cref="EntityCodeSegment"/> children.
/// </para>
/// </summary>
public sealed class EntityCodeRule : IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion, IHybridTenantEntity
{
    private readonly List<EntityCodeSegment> _segments = [];

    private EntityCodeRule() { }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? TenantId { get; private set; }

    Guid? IHybridTenantEntity.TenantId
    {
        get => TenantId;
        set => TenantId = value;
    }

    internal void SetTenant(Guid? tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException(
                "Guid.Empty is not a valid EntityCodeRule tenant. Use null for a shared " +
                "blueprint or a real tenant id for a tenant-owned row.", nameof(tenantId));
        TenantId = tenantId;
    }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<EntityCodeSegment> Segments => _segments.AsReadOnly();

    /// <summary>Factory for a new rule. Segments are added via <see cref="AddSegment"/>.</summary>
    public static EntityCodeRule Create(string code, string name, string? description, bool isActive)
    {
        var now = DateTimeOffset.UtcNow;
        return new EntityCodeRule
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name,
            Description = description,
            IsActive = isActive,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public EntityCodeSegment AddSegment(EntityCodeSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        _segments.Add(segment);
        UpdatedAt = DateTimeOffset.UtcNow;
        return segment;
    }

    /// <summary>
    /// Replaces the entire segment list with <paramref name="segments"/>. Used by the
    /// admin update command when an admin edits the rule's template (the segment
    /// editor sends a full ordered list). Per-segment runtime state
    /// (<c>LastSequence</c>/<c>LastPrefix</c>/<c>LastPeriodBucket</c>) is reset by
    /// the freshly-constructed segments, so changing the template restarts the
    /// sequence. Indices must be unique within the rule.
    /// </summary>
    public void ReplaceSegments(IEnumerable<EntityCodeSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        _segments.Clear();
        var seenIndices = new HashSet<int>();
        foreach (var s in segments)
        {
            if (!seenIndices.Add(s.Index))
                throw new ArgumentException($"Duplicate segment index {s.Index}.", nameof(segments));
            _segments.Add(s);
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates the rule's editable metadata (<c>Name</c>, <c>Description</c>,
    /// <c>IsActive</c>). <c>Code</c> is immutable so rule lookups by the
    /// generator remain stable across edits.
    /// </summary>
    public void Update(string name, string? description, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        Name = name;
        Description = description;
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Delete()
    {
        if (IsDeleted) return;
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Recover()
    {
        if (!IsDeleted) return;
        IsDeleted = false;
        DeletedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Advances every segment for the period containing <paramref name="now"/>
    /// (in index order) and returns the concatenated generated code. Mutates
    /// per-segment sequence state; the caller persists the rule.
    /// </summary>
    public string GenerateNext(DateTimeOffset now, string? nameHint = null)
    {
        var ordered = _segments.OrderBy(s => s.Index).ToList();
        if (ordered.Count == 0)
            throw new EntityCodeGenerationException(Code, "rule has no segments");

        var parts = new List<string>(ordered.Count);
        foreach (var segment in ordered)
        {
            segment.NameHint = nameHint;
            parts.Add(segment.Advance(now));
        }

        return string.Concat(parts);
    }

    /// <summary>
    /// Like <see cref="GenerateNext"/>, but renders each segment with the
    /// supplied <paramref name="overridesBySegment"/> applied to its
    /// FORMAT fields (FixedText / Prefix / Suffix) at render time. The
    /// persisted segment's sequence state (LastSequence / LastPrefix /
    /// LastPeriodBucket) is still mutated in place by
    /// <see cref="EntityCodeSegment.Advance"/> — that state is the
    /// SHARED counter all tenants race on (per §1.2 non-goal: per-tenant
    /// sequence counters are out of scope in v1). The tenant-specific
    /// format override is applied at render time only, so a SHARED rule
    /// (TenantId == null) is never permanently mutated by a tenant's
    /// override — the next tenant sees the original format (just the
    /// shared sequence counter advances).
    /// <para>
    /// Unknown segment ids in <paramref name="overridesBySegment"/> are
    /// silently ignored — they may target a segment that was removed
    /// between when the override was saved and when generation runs.
    /// </para>
    /// </summary>
    public string GenerateNextWithOverrides(
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<OverrideField, string>> overridesBySegment,
        string? nameHint = null)
    {
        ArgumentNullException.ThrowIfNull(overridesBySegment);

        var ordered = _segments.OrderBy(s => s.Index).ToList();
        if (ordered.Count == 0)
            throw new EntityCodeGenerationException(Code, "rule has no segments");

        // Two fields (Prefix, ResetPeriod) are read by EntityCodeSegment.Advance
        // — specifically when computing the period bucket and the initial
        // letter for a new period. We transiently override them so the
        // tenant's format takes effect during the period-reset logic, then
        // restore the persisted values so the shared rule is not mutated.
        // Everything else (FixedText, Suffix, MinWidth, UpperLimit) is
        // applied at render time only via EntityCodeSegment.RenderWithOverrides.
        var segmentsWithPrefixOverride = new List<(EntityCodeSegment Segment, string Persisted)>();
        var segmentsWithResetOverride = new List<(EntityCodeSegment Segment, ResetPeriod Persisted)>();

        try
        {
            foreach (var segment in ordered)
            {
                overridesBySegment.TryGetValue(segment.Id, out var fields);

                if (fields is not null
                    && fields.TryGetValue(OverrideField.Prefix, out var p)
                    && !string.IsNullOrEmpty(p)
                    && segment.Type != SegmentType.Fixed)
                {
                    segmentsWithPrefixOverride.Add((segment, segment.Prefix));
                    segment.SetPrefix(p);
                }

                if (fields is not null
                    && fields.TryGetValue(OverrideField.ResetPeriod, out var rpStr)
                    && int.TryParse(rpStr, out var rp)
                    && Enum.IsDefined(typeof(ResetPeriod), rp)
                    && segment.Type != SegmentType.Fixed)
                {
                    segmentsWithResetOverride.Add((segment, segment.ResetPeriod));
                    segment.SetResetPeriod((ResetPeriod)rp);
                }
            }

            var parts = new List<string>(ordered.Count);
            foreach (var segment in ordered)
            {
                overridesBySegment.TryGetValue(segment.Id, out var fields);
                segment.NameHint = nameHint;
                segment.Advance(now);
                parts.Add(segment.RenderWithOverrides(fields));
            }

            return string.Concat(parts);
        }
        finally
        {
            foreach (var (seg, persisted) in segmentsWithPrefixOverride)
                seg.SetPrefix(persisted);
            foreach (var (seg, persisted) in segmentsWithResetOverride)
                seg.SetResetPeriod(persisted);
        }
    }
}
