using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// Registers the mobile assignments slice — the typed client and the two screen view models — against
/// the configured Assignments API base address and the session's bearer handler.
/// </summary>
/// <remarks>
/// It lives in <c>Mobile.Logic</c> rather than in the head's <c>MauiProgram</c> because the Logic split
/// is load-bearing ("everything CI has to see lives here and only host wiring stays in the head",
/// <c>SchoolCollab.Mobile.Logic.csproj:8-10</c>) and it makes the base address, the handler pipeline and
/// the view-model lifetimes unit-testable on ubuntu. The head's pages <c>@inject</c> the view models, so
/// a missing registration here is a runtime throw rather than a compile error — the registration test
/// pins both.
/// </remarks>
public static class MobileAssignmentsClientRegistration
{
    /// <summary>
    /// Registers <see cref="MobileAssignmentsApiClient"/> bound to
    /// <see cref="MobileApiOptions.AssignmentsApi"/> and carrying
    /// <see cref="BearerTokenDelegatingHandler"/>, plus the two assignments view models the mobile
    /// screens inject.
    /// </summary>
    /// <remarks>
    /// The view models are <b>transient</b>, deliberately: each Blazor component instance gets its own
    /// instance, so optimistic row state cannot survive a navigation. <c>Scoped</c> is NOT the same
    /// thing in a Blazor Hybrid host — the webview resolves scoped services from one app-wide scope,
    /// which is singleton-like, and the list screen's optimistic flips would leak across visits.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="MobileApiOptions.Resolve"/> rejects
    /// <see cref="MobileApiOptions.AssignmentsApi"/> — unreachable while the name below matches
    /// <see cref="MobileApiOptions.ApiNames"/>.</exception>
    public static IServiceCollection AddMobileAssignmentsApiClient(
        this IServiceCollection services, MobileApiOptions apiOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(apiOptions);

        var baseAddress = apiOptions.Resolve(nameof(MobileApiOptions.AssignmentsApi));

        // Transient and idempotent: IHttpClientFactory builds one handler per outgoing request, and
        // L3's AddMobileAdminClients registers the same type for its own client group.
        services.TryAddTransient<BearerTokenDelegatingHandler>();

        services
            .AddHttpClient<MobileAssignmentsApiClient>(client => client.BaseAddress = baseAddress)
            .AddHttpMessageHandler<BearerTokenDelegatingHandler>();

        services.AddTransient<AssignmentsListViewModel>();
        services.AddTransient<AssignmentDetailViewModel>();

        return services;
    }
}
