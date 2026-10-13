using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile.Tests.Unit;

/// <summary>
/// Covers <see cref="MobileClientRegistrationExtensions"/> — the client-side replacement for the
/// module <c>ModuleServices.cs</c> registrations. Two contracts are pinned here: the registered
/// clients resolve against the ONE configured Settings API name (an invented per-client name would
/// throw straight out of <see cref="MobileApiOptions.Resolve"/> at registration time), and the
/// pipeline a registered client actually uses carries both the configured base address and the
/// session's bearer token.
/// </summary>
[TestClass]
public class MobileClientRegistrationTests
{
    private const int SettingsApiPort = 7460;

    private static readonly string SettingsBaseAddress =
        new UriBuilder(Uri.UriSchemeHttps, "settings.example", SettingsApiPort).Uri.ToString();

    private static readonly Type[] AdminSharedClients =
    [
        typeof(CodedValuesApiClient),
        typeof(ConfigFlagsApiClient),
        typeof(TenantsApiClient),
        typeof(EntityCodeRulesApiClient),
        typeof(NotificationPolicyApiClient),
        typeof(AssignmentPolicyApiClient),
        typeof(SignatureConsentTextApiClient),
        typeof(AssignmentAiPromptApiClient),
    ];

    [TestMethod]
    public void AddMobileAdminClients_RegistersEveryAdminSharedClientTheHeadCanReach()
    {
        using var provider = BuildProvider(SettingsApi(SettingsBaseAddress));

        foreach (var clientType in AdminSharedClients)
        {
            // Constructing the typed client builds its whole handler pipeline — so this resolution both
            // proves the registration and that the bearer handler in it is resolvable.
            provider.GetRequiredService(clientType).Should().NotBeNull();
        }
    }

    [TestMethod]
    public async Task AddMobileAdminClients_SendsThroughTheConfiguredSettingsAddress_WithTheSessionsBearerToken()
    {
        var primaryHandler = new RecordingHandler();
        using var provider = BuildProvider(SettingsApi(SettingsBaseAddress), primaryHandler);
        await provider.GetRequiredService<ITokenStore>().WriteAsync(TokenKeys.AccessToken, "session-access-token");
        await provider.GetRequiredService<ITokenStore>().WriteAsync(
            TokenKeys.ExpiresAt,
            DateTimeOffset.UtcNow.AddMinutes(5).ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        var values = await provider.GetRequiredService<CodedValuesApiClient>().GetRootValuesAsync();

        values.Should().BeEmpty();
        primaryHandler.Requests.Should().ContainSingle();
        var request = primaryHandler.Requests[0];
        request.RequestUri!.Host.Should().Be("settings.example",
            "the client's base address comes from the ONE Settings API name this registration resolves.");
        request.RequestUri!.Port.Should().Be(SettingsApiPort);
        request.RequestUri!.AbsolutePath.Should().Be("/api/coded-values",
            "the relative request path is appended to the configured base address.");
        primaryHandler.AuthorizationHeaders.Should().ContainSingle()
            .Which.Should().Be("Bearer session-access-token",
                "the bearer handler is in the registered client's pipeline, so the session's token travels with the call.");
    }

    [TestMethod]
    public async Task AddMobileAdminClients_DegradesTheApi_WhenTheSettingsAddressIsUnconfigured()
    {
        using var provider = BuildProvider(new MobileApiOptions());

        var call = () => provider.GetRequiredService<CodedValuesApiClient>().GetRootValuesAsync();

        (await call.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*BaseAddress*");
    }

    [TestMethod]
    public void AddMobileAdminClients_RegistersTheBearerHandler_Transiently()
    {
        using var provider = BuildProvider(SettingsApi(SettingsBaseAddress));

        var first = provider.GetRequiredService<BearerTokenDelegatingHandler>();
        var second = provider.GetRequiredService<BearerTokenDelegatingHandler>();

        first.Should().NotBeSameAs(second,
            "a delegating handler is built per outgoing request via IHttpClientFactory, so it must be transient and hold no per-request state.");
    }

    [TestMethod]
    public void AddMobileAdminClients_RejectsANullApiOptions()
    {
        var services = new ServiceCollection();

        var register = () => services.AddMobileAdminClients(null!);

        register.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void AddMobileAdminClients_RegistersTheTenantServiceTheGatesNeed()
    {
        using var provider = BuildProvider(SettingsApi(SettingsBaseAddress));

        provider.GetRequiredService<VisibleTenantService>().Should().NotBeNull();
    }

    [TestMethod]
    public void AddMobileAuth_RegistersTheProvider_AsTheSingleInstanceTheGatesInject()
    {
        using var provider = BuildProvider(SettingsApi(SettingsBaseAddress));

        var concrete = provider.GetRequiredService<MobileAuthenticationStateProvider>();
        var abstraction = provider.GetRequiredService<AuthenticationStateProvider>();

        abstraction.Should().BeSameAs(concrete,
            "the gates subscribe to AuthenticationStateChanged on the injected instance; a second instance would be a second, silent source of auth state.");
    }

    [TestMethod]
    public async Task AddMobileAuth_ResolvesTheAuthPath_WithTheNamedTokenClientBoundToTheAuthority()
    {
        using var provider = BuildProvider(SettingsApi(SettingsBaseAddress));

        provider.GetRequiredService<OidcLoginService>().Should().NotBeNull();
        await provider.GetRequiredService<TokenSession>().EnsureInitializedAsync();

        var tokenClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TokenSession.HttpClientName);
        tokenClient.BaseAddress.Should().Be(new Uri($"{AuthAuthority}/"),
            "the named token client's base address is the configured authority — never a C# literal (spec D8).");
    }

    [TestMethod]
    public void AddMobileAuth_RejectsANullAuthOptions()
    {
        var services = new ServiceCollection();

        var register = () => services.AddMobileAuth(null!);

        register.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// The head's real composition, minus the two platform implementations (they are head-only types):
    /// the auth path, the client registrations, and the logging/token seams the container needs.
    /// </summary>
    private static ServiceProvider BuildProvider(MobileApiOptions apiOptions, RecordingHandler? primaryHandler = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ITokenStore, FakeTokenStore>();
        services.AddSingleton<ISystemBrowserAuthenticator, FakeAuthenticator>();

        var authOptions = MobileAuthOptions.FromConfiguration(BuildConfiguration(
            ("MobileAuth:Authority", AuthAuthority),
            ("MobileAuth:ClientId", "school-collab-mobile-client"),
            ("MobileAuth:RedirectScheme", "schoolcollab"),
            ("MobileAuth:RedirectHost", "signin-oidc")));

        services.AddMobileAuth(authOptions);
        services.AddMobileAdminClients(apiOptions);

        if (primaryHandler is not null)
        {
            // Configure the SAME typed client the registration created (identical name derivation), so the
            // test sees exactly the request the registered pipeline produces.
            services.AddHttpClient<CodedValuesApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>The authority is written from <see cref="UriBuilder"/>, never typed as a literal, so
    /// this suite cannot be the place the D8 rule rots.</summary>
    private static string AuthAuthority =>
        new UriBuilder(Uri.UriSchemeHttps, "idp.example", 8080, "realms/school-collab").Uri.ToString();

    private static MobileApiOptions SettingsApi(string baseAddress) =>
        MobileApiOptions.FromConfiguration(BuildConfiguration(("MobileApi:SettingsApi", baseAddress)));

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    /// <summary>The far end of a registered client's pipeline: records what actually went out.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> AuthorizationHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            AuthorizationHeaders.Add(request.Headers.TryGetValues("Authorization", out var values)
                ? string.Join(",", values)
                : string.Empty);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FakeTokenStore : ITokenStore
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task ClearAsync(string key, CancellationToken cancellationToken = default)
        {
            _values[key] = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthenticator : ISystemBrowserAuthenticator
    {
        public Task<IDictionary<string, string>> AuthenticateAsync(
            Uri authorizeUrl,
            Uri callbackUri,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IDictionary<string, string>>(new Dictionary<string, string>(StringComparer.Ordinal));
    }
}
