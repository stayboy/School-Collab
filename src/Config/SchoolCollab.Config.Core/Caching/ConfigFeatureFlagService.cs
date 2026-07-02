using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolCollab.Config.Core.DTOs;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Config.Core.Caching;

/// <summary>
/// Cached, DB-backed <see cref="IFeatureFlagService"/>. Resolves the whole flag
/// set per tenant through a HybridCache L1 (in-proc, 30s) + L2 (Redis, 5min)
/// backed by an HTTP call to the Config API. Falls back to <see cref="IConfiguration"/>
/// when the API is unreachable (preserving the "works without Config running" dev
/// behaviour). Propagation of runtime changes is bounded by the L1/L2 TTLs (the
/// "sensible ITL" floor); a push invalidation subscriber is a follow-up (v1.1).
/// </summary>
public sealed class ConfigFeatureFlagService : IFeatureFlagService, IFeatureFlagResolver
{
    internal const string HttpClientName = "config-api";

    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
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
                    // OrdinalIgnoreCase so callers can look up with the canonical upper-case
                    // form produced by FeatureFlag.NormalizeKey (e.g.
                    // "FEATURE:ENABLECODEDVALUESAICHAT") OR with the humanised mixed-case
                    // form most call sites use (e.g. "FEATURE:EnableCodedValuesAiChat").
                    // The DB stores the canonical upper-case form; the appsettings fallback
                    // walks IConfiguration and preserves the keys as-authored. Without the
                    // case-insensitive comparer a mixed-case lookup against the DB-backed
                    // path silently misses and the flag evaluates as disabled.
                    return (IReadOnlyDictionary<string, ResolvedFlag>)new Dictionary<string, ResolvedFlag>(
                        dtos!.ToDictionary(d => d.Key, d => new ResolvedFlag(d.Key, d.IsEnabled, ParseSource(d.Source), d.ResolvedAt)),
                        StringComparer.OrdinalIgnoreCase);
                },
                CacheOptions,
                cancellationToken: ct);

            return fromCache;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            WarnOnce(cacheKey, "Config API unreachable; falling back to IConfiguration for flags.");
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