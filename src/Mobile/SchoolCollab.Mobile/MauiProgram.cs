using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Mobile.Logic;
using SchoolCollab.Mobile.Services;

namespace SchoolCollab.Mobile;

public static class MauiProgram
{
    /// <summary>
    /// The embedded copy of <c>appsettings.json</c>. MAUI adds no configuration sources of
    /// its own, so this is how the head's base URLs (spec D8: configuration, never a C#
    /// literal) reach the app.
    /// </summary>
    private const string AppSettingsResourceName = "SchoolCollab.Mobile.appsettings.json";

    /// <summary>
    /// The embedded <c>appsettings.Development.json</c> — the dev-only overlay (the dev IdP's
    /// authority). Streamed in ONLY in the Development environment, so a non-dev build cannot
    /// silently point at a development identity provider.
    /// </summary>
    private const string DevelopmentAppSettingsResourceName = "SchoolCollab.Mobile.appsettings.Development.json";

    /// <summary>The environment variable MAUI/.NET uses to name the app environment.</summary>
    private const string EnvironmentVariableName = "DOTNET_ENVIRONMENT";

    /// <summary>The environment name that loads <see cref="DevelopmentAppSettingsResourceName"/>.</summary>
    private const string DevelopmentEnvironmentName = "Development";

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        AddAppSettings(builder);

        var apiOptions = MobileApiOptions.FromConfiguration(builder.Configuration);
        var authOptions = MobileAuthOptions.FromConfiguration(builder.Configuration);

        builder.Services.AddSingleton(apiOptions);
        builder.Services.AddSingleton(authOptions);

        // The two platform implementations of Mobile.Logic's auth seams. MAUI Essentials types
        // (SecureStorage, WebAuthenticator) compile only in the head, which is exactly why the
        // seams exist.
        builder.Services.AddSingleton<ITokenStore, SecureStorageTokenStore>();
        builder.Services.AddSingleton<ISystemBrowserAuthenticator, SystemBrowserAuthenticator>();

        builder.Services
            .AddMobileAuth(authOptions)
            .AddMobileAdminClients(apiOptions)
            .AddMobileAssignmentsApiClient(apiOptions);

        // The FluentUI services the screens resolve (IDialogService backs the confirm-gated delete; the
        // provider it renders into is in MainLayout) and the authorization services <AuthorizeView>
        // needs. BOTH authorization registrations are required and neither adds a package: they live in
        // Microsoft.AspNetCore.Components.Authorization, which Mobile.Logic already references.
        // AddAuthorizationCore supplies IAuthorizationService/IAuthorizationPolicyProvider;
        // AddCascadingAuthenticationState supplies the cascading Task<AuthenticationState> that
        // AuthorizeViewCore resolves or throws on (verified against the 10.0.8 assembly).
        builder.Services.AddFluentUIComponents();
        builder.Services.AddAuthorizationCore();
        builder.Services.AddCascadingAuthenticationState();

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Streams the head's embedded <c>appsettings.json</c> into
    /// <paramref name="builder"/>'s configuration, plus the Development overlay when
    /// <see cref="EnvironmentVariableName"/> names <see cref="DevelopmentEnvironmentName"/>.
    /// </summary>
    /// <remarks>
    /// The environment is read from the process environment variable rather than from the
    /// builder: <see cref="MauiAppBuilder"/> exposes <c>Configuration</c>, <c>Services</c> and
    /// <c>Logging</c> and has NO <c>Environment</c> property, and resolving <c>IHostEnvironment</c>
    /// from the built host would be too late to add a configuration source at all.
    /// </remarks>
    /// <exception cref="InvalidOperationException">An embedded resource is missing from the assembly.</exception>
    private static void AddAppSettings(MauiAppBuilder builder)
    {
        AddJsonResource(builder, AppSettingsResourceName);

        if (Environment.GetEnvironmentVariable(EnvironmentVariableName) == DevelopmentEnvironmentName)
        {
            AddJsonResource(builder, DevelopmentAppSettingsResourceName);
        }
    }

    /// <summary>Streams one embedded JSON resource into <paramref name="builder"/>'s configuration.</summary>
    /// <exception cref="InvalidOperationException">The embedded resource is missing from the assembly.</exception>
    private static void AddJsonResource(MauiAppBuilder builder, string resourceName)
    {
        var assembly = typeof(MauiProgram).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' is missing — check the " +
                "EmbeddedResource item in SchoolCollab.Mobile.csproj.");

        builder.Configuration.AddJsonStream(stream);
    }
}
