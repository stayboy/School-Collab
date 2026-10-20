using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.AI.Chat.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Http;

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
        // (The handler type was already TryAddTransient above — a second
        // TryAdd* here is a no-op; all DelegatingHandlers MUST stay TRANSIENT.)
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

        // AssignmentPolicyApiClient (tenant-global default guardian-signature
        // policy) — same settings-api base address. TenantAssignmentPolicy is a
        // STRICT tenant entity: reads/writes MUST resolve the selected tenant or
        // they hit the wrong tenant (same symptom class as the notification
        // client above). WS-C1 / spec §7 Q1.
        services.AddHttpClient<AssignmentPolicyApiClient>(client =>
            client.BaseAddress = new Uri("https+http://settings-api"))
            .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();

        // NotificationTemplateApiClient (notification-template store CRUD + the grade-detail
        // applicability read) — same settings-api base address. TenantNotificationTemplate is a
        // STRICT tenant entity: reads/writes MUST resolve the selected tenant or they hit the wrong
        // tenant (same symptom class as the clients above). Spec §5 / §3.11, round B3a.
        services.AddHttpClient<NotificationTemplateApiClient>(client =>
            client.BaseAddress = new Uri("https+http://settings-api"))
            .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();

        // NotificationTemplatePreviewClient (the template editor's live preview, spec §3.10) —
        // CROSS-MODULE: admin → assignments-api, the one host that owns the renderer
        // (NotificationTemplateRenderer lives in Assignments.Core, which Settings may not
        // reference; a second substituter in the UI is the drift class §3.10 forbids). The Admin
        // host already carries .WithReference(assignmentsApi) (AppHost/Program.cs:566), so no
        // AppHost change is needed. The explicit ~5 s timeout is the degradation budget: a dead
        // preview shows the editor's inline "preview unavailable" in seconds, not after the 100 s
        // HttpClient default. It is an enrichment read only — a failure never blocks save.
        services.AddCrossModuleHttpClient<NotificationTemplatePreviewClient>(
                "https+http://assignments-api", propagateTenant: true)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));

        // StudentsLookupClient (the constraint editor's grade-level / activity-group / topic
        // pickers, spec §3.10) — CROSS-MODULE read: admin → students-api. The matcher compares
        // assignment target reference ids (grade-level id, topic id, activity-group id), which the
        // Settings codebook cannot produce, so the pickers must read them from their owning
        // context. An enrichment read under adr-cross-module-calls.md: the editor degrades to an
        // inline error + raw-value rendering when it fails. The Admin host already carries
        // .WithReference(studentsApi) (AppHost/Program.cs:567).
        services.AddCrossModuleHttpClient<StudentsLookupClient>(
            "https+http://students-api", propagateTenant: true);

        // SignatureConsentTextApiClient (tenant-level guardian sign-off consent
        // text) — same pattern. TenantSignatureConsentText is a STRICT tenant
        // entity: reads/writes MUST resolve the selected tenant or they hit the
        // wrong tenant (same symptom class as the clients above). WS-C2 / spec
        // §3.2.
        services.AddHttpClient<SignatureConsentTextApiClient>(client =>
            client.BaseAddress = new Uri("https+http://settings-api"))
            .AddHttpMessageHandler<TenantPropagationDelegatingHandler>();

        // AssignmentAiPromptApiClient (tenant-level organization AI prompt for
        // assignment question generation) — same pattern.
        // TenantAssignmentAiPrompt is a STRICT tenant entity: reads/writes MUST
        // resolve the selected tenant or they hit the wrong tenant (same symptom
        // class as the clients above). WS-B2 / spec §3.4.
        services.AddHttpClient<AssignmentAiPromptApiClient>(client =>
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