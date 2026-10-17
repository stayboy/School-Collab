using System.Text.Json;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;

namespace SchoolCollab.Admin.Shared.Components.Theme;

/// <summary>
/// One-time migration of a pre-v5 theme preference into the FluentUI v5 theme store.
/// </summary>
/// <remarks>
/// v4's <c>FluentDesignTheme</c> persisted the chosen mode under a host-supplied localStorage
/// key (<see cref="AdminStorageKey"/> / <see cref="FamiliesStorageKey"/>). v5 dropped that
/// component, and the library's own JS now owns theming state in its
/// <c>fluentui-blazor:theme-settings</c> store, applied at startup — so a returning user
/// would silently fall back to the default theme. Each host therefore calls
/// <see cref="RunAsync"/> once, from the first render of its theming surface.
///
/// <para>Two guards keep this from running twice or fighting a user change: the legacy key is
/// deleted as soon as it has been read, and the legacy settings are applied only while the v5
/// store is still empty — a store that already carries settings is the newer choice and wins.
/// The mode is carried over as <see cref="ThemeMode"/> and the accent (<c>primaryColor</c>) as a
/// brand colour through <see cref="IThemeService.SetThemeAsync(string, bool)"/>, resolved by
/// <see cref="ThemeAccents.ResolveLegacyColor"/> (v4 stored either a hex or an
/// <c>OfficeColor</c> name); v4's <c>neutralColor</c> has no v5 equivalent and is dropped.</para>
/// </remarks>
public static class LegacyThemeMigration
{
    /// <summary>The localStorage key the Admin host's v4 theme root wrote (<c>StorageName="theme"</c>).</summary>
    public const string AdminStorageKey = "theme";

    /// <summary>The localStorage key the Families host's v4 theme root wrote (<c>StorageName="families-theme"</c>).</summary>
    public const string FamiliesStorageKey = "families-theme";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Applies the legacy theme held under <paramref name="storageKey"/> through
    /// <paramref name="themeService"/> and then deletes the key. A no-op when the key holds
    /// nothing, and a delete-only no-op when the v5 store already carries settings.
    /// </summary>
    public static async Task RunAsync(IJSRuntime js, IThemeService themeService, string storageKey)
    {
        var legacyJson = await js.InvokeAsync<string?>("localStorage.getItem", storageKey);
        if (string.IsNullOrEmpty(legacyJson))
        {
            return;
        }

        if (await themeService.GetThemeSettingsAsync() is null)
        {
            var legacy = Parse(legacyJson);
            if (ToThemeMode(legacy?.Mode) is { } mode)
            {
                await themeService.SetThemeAsync(mode);
            }

            // isExact mirrors the accent picker: the migrated choice is the brand colour itself,
            // not a ramp generated from it.
            if (ThemeAccents.ResolveLegacyColor(legacy?.PrimaryColor) is { } color)
            {
                await themeService.SetThemeAsync(color, isExact: true);
            }
        }

        await js.InvokeVoidAsync("localStorage.removeItem", storageKey);
    }

    /// <summary>
    /// Deserializes v4's <c>{ "mode": …, "primaryColor": …, "neutralColor": … }</c> payload.
    /// Returns <see langword="null"/> when the text cannot be read at all (the caller still
    /// deletes the key in that case). The shape is confirmed against v4.14.2's own JS, which
    /// writes exactly those three properties under the host-supplied storage name.
    /// </summary>
    private static LegacyTheme? Parse(string legacyJson)
    {
        try
        {
            return JsonSerializer.Deserialize<LegacyTheme>(legacyJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps v4's mode string to a <see cref="ThemeMode"/>. <c>null</c> meant v4's System mode, and
    /// an absent or unrecognised value carries no preference — returning System for either would
    /// clear a preference rather than carry one over, so both yield <see langword="null"/>.
    /// </summary>
    private static ThemeMode? ToThemeMode(string? legacyMode) => legacyMode?.ToLowerInvariant() switch
    {
        "dark" => ThemeMode.Dark,
        "light" => ThemeMode.Light,
        _ => null,
    };

    private sealed record LegacyTheme(string? Mode, string? PrimaryColor);
}
