using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Caching;

/// <summary>
/// Cached, DB-backed <see cref="IFeatureFlagService"/>. Resolves the whole flag
/// set per tenant through a HybridCache L1 (in-proc, 5s) + L2 (Redis, 30s)
/// backed by an HTTP call to the Settings API. Falls back to <see cref="IConfiguration"/>
/// when the API is unreachable (preserving the "works without Settings running" dev
/// behaviour). Propagation of runtime changes is bounded by the L1/L2 TTLs (the
/// "sensible ITL" floor); a push invalidation subscriber is a follow-up (v1.1).
/// </summary>
public sealed class ConfigFeatureFlagService : IFeatureFlagService, IFeatureFlagResolver
{
    /// <summary>
    /// Named HttpClient key in the consumer host's DI container. The
    /// <see cref="ConfigFeatureFlagClientExtensions.AddConfigFeatureFlagClient"/>
    /// extension registers a named HttpClient under this key with its
    /// <c>BaseAddress</c> set to the Aspire service-discovery URL of the
    /// unified <c>settings-api</c> resource (was <c>config-api</c> before the
    /// Settings context merge).
    /// </summary>
    internal const string HttpClientName = "settings-api";

    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        // Short TTLs so a flag change made via the Config admin UI propagates to
        // consumer hosts within ~30s without a restart. The push-invalidation
        // subscriber (v1.1) will collapse this to near-zero once wired up; until
        // then the L2 ceiling is the worst-case propagation delay a human tester
        // sees after toggling a flag.
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(5)
    };

    private readonly HybridCache _cache;
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<ConfigFeatureFlagService> _logger;
    private readonly HashSet<string> _warnedFallbackKeys = new();
    private readonly object _warnLock = new();

    public ConfigFeatureFlagService(
        HybridCache cache,
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ITenantProvider tenantProvider,
        ILogger<ConfigFeatureFlagService> logger)
    {
        _cache = cache;
        _http = httpFactory.CreateClient(HttpClientName);
        _configuration = configuration;
        _tenantProvider = tenantProvider;
        _logger = logger;
    }

    // ── IFeatureFlagService (sync) — delegate to the async tenant-aware path ──

    public bool IsEnabled(string featureKey) =>
        IsEnabledAsync(featureKey, CancellationToken.None).GetAwaiter().GetResult();

    public IDictionary<string, bool> GetAllFlags()
    {
        var all = GetAllFlagsAsync(tenantId: null, CancellationToken.None).GetAwaiter().GetResult();
        return all.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public async Task<bool> IsEnabledAsync(string key, CancellationToken ct = default)
    {
        var tenantId = SafeTenantId();
        var all = await ResolveAllAsync(tenantId, ct);
        return all.TryGetValue(key, out var flag) && flag.IsEnabled;
    }

    public async Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
    {
        var all = await ResolveAllAsync(tenantId, ct);
        return all.ToDictionary(f => f.Key, f => f.Value.IsEnabled);
    }

    // ── IFeatureFlagResolver ──

    public async Task<ResolvedFlag> ResolveAsync(string key, Guid? tenantId, CancellationToken ct = default)
    {
        var all = await ResolveAllAsync(tenantId, ct);
        return all.TryGetValue(key, out var flag)
            ? flag
            : FromConfiguration(key, ResolvedFlagSource.ConfigurationFallback);
    }

    public async Task<IReadOnlyDictionary<string, ResolvedFlag>> ResolveAllAsync(Guid? tenantId, CancellationToken ct = default)
    {
        var tenant = tenantId ?? SafeTenantId();
        var cacheKey = CacheKeys.AllFlags(tenant);

        try
        {
            var fromCache = await _cache.GetOrCreateAsync(
                cacheKey,
                async _ =>
                {
                    var route = tenant is null ? "/api/features/global" : $"/api/features/{tenant}";
                    var dtos = await _http.GetFromJsonAsync<ResolvedFlagDto[]>(route, ct);
                    var now = DateTimeOffset.UtcNow;
                    // Build with OrdinalIgnoreCase so the in-memory (L1) copy lets callers
                    // look up with the canonical upper-case form produced by
                    // FeatureFlag.NormalizeKey (e.g. "FEATURE:ENABLECODEDVALUESAICHAT") OR
                    // the humanised mixed-case form most call sites use (e.g.
                    // "FEATURE:EnableCodedValuesAiChat"). The DB stores the canonical
                    // upper-case form; the appsettings fallback preserves keys as-authored.
                    return (IReadOnlyDictionary<string, ResolvedFlag>)new Dictionary<string, ResolvedFlag>(
                        dtos!.ToDictionary(d => d.Key, d => new ResolvedFlag(d.Key, d.IsEnabled, ParseSource(d.Source), d.ResolvedAt)),
                        StringComparer.OrdinalIgnoreCase);
                },
                CacheOptions,
                cancellationToken: ct);

            // Re-wrap with OrdinalIgnoreCase on every read. HybridCache serializes
            // the cached dictionary through its L2 (Redis) / L1 serializer on a
            // round-trip; the deserialized copy is rebuilt as a plain
            // Dictionary<string, ResolvedFlag> with the DEFAULT (case-sensitive)
            // comparer, so the OrdinalIgnoreCase comparer set in the factory above
            // is silently lost on a cache hit after L1 expires. A mixed-case
            // caller key ("FEATURE:EnableCodedValuesAiChat") then misses the
            // stored canonical key ("FEATURE:ENABLECODEDVALUESAICHAT") and the
            // flag evaluates as disabled even though it is enabled in the DB —
            // the exact "chat won't render" symptom. Re-wrapping here guarantees a
            // case-insensitive lookup regardless of how the cache returned the
            // dictionary. Cost is a small copy per call (a handful of flags).
            return WithCaseInsensitiveLookup(fromCache);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            WarnOnce(cacheKey, "Settings API unreachable; falling back to IConfiguration for flags.");
            return FromConfigurationAll(ResolvedFlagSource.ConfigurationFallback);
        }
    }

    // ── fallback ──

    private IReadOnlyDictionary<string, ResolvedFlag> FromConfigurationAll(ResolvedFlagSource source)
    {
        // OrdinalIgnoreCase so callers can look up with the canonical upper-case
        // form produced by FeatureFlag.NormalizeKey OR the humanised mixed-case
        // form most call sites use. See ResolveAllAsync for the full rationale.
        var flags = new Dictionary<string, ResolvedFlag>(StringComparer.OrdinalIgnoreCase);
        var section = _configuration.GetSection("FeatureFlags");
        Collect(section, flags, prefix: null, source);
        return flags;
    }

    private void Collect(IConfigurationSection section, Dictionary<string, ResolvedFlag> flags, string? prefix, ResolvedFlagSource source)
    {
        foreach (var child in section.GetChildren())
        {
            var key = prefix is null ? child.Key : $"{prefix}:{child.Key}";
            if (!string.IsNullOrEmpty(child.Value) && bool.TryParse(child.Value, out var enabled))
            {
                flags[key] = new ResolvedFlag(key, enabled, source, DateTimeOffset.UtcNow);
            }
            else
            {
                Collect(child, flags, key, source);
            }
        }
    }

    private ResolvedFlag FromConfiguration(string key, ResolvedFlagSource source)
    {
        var value = _configuration[$"FeatureFlags:{key}"] ?? _configuration[key];
        var enabled = bool.TryParse(value, out var b) && b;
        return new ResolvedFlag(key, enabled, source, DateTimeOffset.UtcNow);
    }

    // ── helpers ──

    /// <summary>
    /// Re-wraps the resolved flag set in a dictionary keyed with
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>. <see cref="ResolveAllAsync"/>
    /// calls this on every read because HybridCache serializes the cached
    /// dictionary through its L2/L1 serializer on a round-trip, and the
    /// deserialized copy is rebuilt with the DEFAULT (case-sensitive) comparer —
    /// silently discarding the OrdinalIgnoreCase comparer the factory set. A
    /// mixed-case caller key (e.g. "FEATURE:EnableCodedValuesAiChat") would then
    /// miss the stored canonical key ("FEATURE:ENABLECODEDVALUESAICHAT") and the
    /// flag would evaluate as disabled. Exposed as internal static so the
    /// behaviour is unit-testable without standing up HybridCache/HttpClient.
    /// </summary>
    internal static IReadOnlyDictionary<string, ResolvedFlag> WithCaseInsensitiveLookup(
        IReadOnlyDictionary<string, ResolvedFlag> flags) =>
        new Dictionary<string, ResolvedFlag>(flags, StringComparer.OrdinalIgnoreCase);

    private Guid? SafeTenantId()
    {
        try
        {
            var ctx = _tenantProvider.GetTenantContext();
            return ctx.TenantId == Guid.Empty ? null : ctx.TenantId;
        }
        catch
        {
            return null;
        }
    }

    private static ResolvedFlagSource ParseSource(string source) => source switch
    {
        "TenantOverride" => ResolvedFlagSource.TenantOverride,
        "GlobalDefault" => ResolvedFlagSource.GlobalDefault,
        _ => ResolvedFlagSource.GlobalDefault,
    };

    private void WarnOnce(string key, string message)
    {
        lock (_warnLock)
        {
            if (_warnedFallbackKeys.Add(key))
            {
                _logger.LogWarning("{Message} (cache key: {CacheKey})", message, key);
            }
        }
    }

    internal static class CacheKeys
    {
        public static string AllFlags(Guid? tenantId) =>
            tenantId is null ? "cfg:flags:GLOBAL" : $"cfg:flags:{tenantId}";
    }
}