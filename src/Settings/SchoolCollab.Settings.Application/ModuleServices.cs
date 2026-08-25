using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.AI.Chat.Services;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Settings.Application;

public static class ModuleServices
{
    /// <summary>
    /// Unified registration for the Settings admin module. Wires the
    /// service-discovered HTTP clients that the Settings admin pages consume:
    /// the CodedValues API client (against <c>settings-api</c>), the ConfigFlags
    /// API client (against <c>settings-api</c>), and the AI chat surface
    /// (against <c>settings-ai</c> — the unified replacement for
    /// <c>coded-values-ai</c>). The AI chat surface (AiChatClient + the scoped
    /// AiChatHub mirror bridge) is wired by the RCL's <c>AddAiChat</c> helper.
    /// Replaces the legacy <c>AddCodedValuesModule()</c> + <c>AddConfigModule()</c>
    /// pair. See documents/solution/settings-context-merge-spec.md §5 and §9.
    /// </summary>
    public static IServiceCollection AddSettingsModule(this IServiceCollection services)
    {
        // CodedValues landing page + drawer chat (the existing
        // CodedValuesApiClient already lives in Admin.Shared, so we just
        // point it at the unified settings-api base address). Tenant
        // propagation: the enroll dialog's grade + stream pickers load
        // tenant-scoped coded values through this client, so the dev-selected
        // tenant must travel with each request (same pattern as
        // StudentsApiClient — see AddStudentsModule). Registered TRANSIENT
        // (not Singleton): IHttpClientFactory sets InnerHandler per named-client
        // pipeline, so a Singleton shared across clients gets corrupted.
        services.TryAddTransient<TenantPropagationDelegatingHandler>();
        services.AddHttpClient<CodedValuesApiClient>(client =>
            client.BaseAddress = new Uri("https+http://settings-api"))
            .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();

        // AI chat surface for the CodedValues drawer: wires AiChatClient
        // (HttpClient against settings-ai) + the scoped AiChatHub mirror bridge.
        services.AddAiChat(client => client.BaseAddress = new Uri("https+http://settings-ai"));

        // ConfigFlagsApiClient (FeatureFlag CRUD UI) — same pattern. Flags are
        // global but per-tenant overrides (TenantFeatureFlagOverride) are
        // strict-tenant scoped, so the tenant must travel with each request.
        services.TryAddSingleton<TenantPropagationDelegatingHandler>();
        services.AddHttpClient<ConfigFlagsApiClient>(client =>
            client.BaseAddress = new Uri("https+http://settings-api"))
            .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();

        // TenantsApiClient (dev tenant switcher dropdown) — read-only GLOBAL
        // tenant registry; intentionally cross-tenant, no header needed.
        services.AddHttpClient<TenantsApiClient>(client =>
            client.BaseAddress = new Uri("https+http://settings-api"));

        // EntityCodeRulesApiClient (auto-generation rule CRUD UI) — same pattern,
        // same settings-api base address. EntityCodeRule is a HYBRID tenant
        // entity: tenant-owned rules must be visible to their owning tenant.
        // Spec §4.11 / §4.7.
        services.AddHttpClient<EntityCodeRulesApiClient>(client =>
            client.BaseAddress = new Uri("https+http://settings-api"))
            .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();

        // NotificationPolicyApiClient (tenant-global default notification policy) —
        // same settings-api base address. TenantNotificationPolicy is a STRICT
        // tenant entity: reads/writes MUST resolve the selected tenant or they hit
        // the default tenant (or trip the TenantContextRequiredException guard).
        // Spec notification-delivery-plan.md §4.
        services.AddHttpClient<NotificationPolicyApiClient>(client =>
            client.BaseAddress = new Uri("https+http://settings-api"))
            .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();

        // VisibleTenantService: read tenant_id claim to decide whether the
        // signed-in user has a real tenant (used by the Edit page to gate
        // the per-tenant override UI). The Students module also registers
        // this service; the duplicate AddScoped is benign because both
        // registrations target the same concrete type with the same
        // constructor — the DI container resolves to a single instance.
        services.AddScoped<VisibleTenantService>();

        return services;
    }
}