using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolCollab.Mobile.Logic;

namespace SchoolCollab.Mobile;

public static class MauiProgram
{
    /// <summary>
    /// The embedded copy of <c>appsettings.json</c>. MAUI adds no configuration sources of
    /// its own, so this is how the head's base URLs (spec D8: configuration, never a C#
    /// literal) reach the app.
    /// </summary>
    private const string AppSettingsResourceName = "SchoolCollab.Mobile.appsettings.json";

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
        builder.Services.AddSingleton(MobileApiOptions.FromConfiguration(builder.Configuration));

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Streams the head's embedded <c>appsettings.json</c> into
    /// <paramref name="builder"/>'s configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">The embedded resource is missing from the assembly.</exception>
    private static void AddAppSettings(MauiAppBuilder builder)
    {
        var assembly = typeof(MauiProgram).Assembly;
        using var stream = assembly.GetManifestResourceStream(AppSettingsResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{AppSettingsResourceName}' is missing — check the " +
                "EmbeddedResource item in SchoolCollab.Mobile.csproj.");

        builder.Configuration.AddJsonStream(stream);
    }
}
