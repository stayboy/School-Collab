using Microsoft.Extensions.Configuration;

namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// The module API base addresses the mobile head talks to, bound from the
/// <see cref="SectionName"/> section of the head's <c>appsettings*.json</c>.
/// </summary>
/// <remarks>
/// Base addresses are configuration, never C# literals (spec D8): one build has to reach the
/// AppHost from an Android emulator (<c>10.0.2.2</c>), a LAN device, and a deployed
/// environment, and a compiled-in host could serve none of those three.
/// <see cref="Resolve"/> normalizes every configured value to an absolute <see cref="Uri"/>
/// with a trailing slash, which is the shape <c>HttpClient.BaseAddress</c> requires before
/// relative request paths are appended.
/// </remarks>
public sealed class MobileApiOptions
{
    /// <summary>The configuration section this class is bound from.</summary>
    public const string SectionName = "MobileApi";

    /// <summary>Every API name this class recognizes, in report order.</summary>
    public static readonly IReadOnlyList<string> ApiNames =
    [
        nameof(StudentsApi),
        nameof(SettingsApi),
        nameof(AssignmentsApi),
    ];

    /// <summary>Base address of the Students API.</summary>
    public string? StudentsApi { get; set; }

    /// <summary>Base address of the Settings API.</summary>
    public string? SettingsApi { get; set; }

    /// <summary>Base address of the Assignments API.</summary>
    public string? AssignmentsApi { get; set; }

    /// <summary>
    /// Binds a new instance from the <see cref="SectionName"/> section of
    /// <paramref name="configuration"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
    public static MobileApiOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new MobileApiOptions();
        configuration.GetSection(SectionName).Bind(options);
        return options;
    }

    /// <summary>
    /// The configured base address of <paramref name="apiName"/>, normalized to an absolute
    /// <see cref="Uri"/> with a trailing slash — or <see langword="null"/> when the value is
    /// absent or blank, so a caller can degrade one API without the whole configuration
    /// failing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="apiName"/> is not one of <see cref="ApiNames"/>.</exception>
    /// <exception cref="InvalidOperationException">The configured value is not an absolute URI.</exception>
    public Uri? Resolve(string apiName)
    {
        var value = RawValue(apiName);
        return string.IsNullOrWhiteSpace(value) ? null : ToBaseAddress(apiName, value);
    }

    /// <summary>Names from <see cref="ApiNames"/> whose base address is not configured.</summary>
    public IReadOnlyList<string> MissingBaseAddresses() =>
        [.. ApiNames.Where(name => Resolve(name) is null)];

    private string? RawValue(string apiName) => apiName switch
    {
        nameof(StudentsApi) => StudentsApi,
        nameof(SettingsApi) => SettingsApi,
        nameof(AssignmentsApi) => AssignmentsApi,
        _ => throw new ArgumentOutOfRangeException(
            nameof(apiName),
            apiName,
            $"Unknown API name; expected one of {string.Join(", ", ApiNames)}."),
    };

    private static Uri ToBaseAddress(string apiName, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{apiName} must be an absolute URL; got '{value}'.");
        }

        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }
}
