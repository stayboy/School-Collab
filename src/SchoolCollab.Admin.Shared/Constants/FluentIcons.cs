using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Admin.Shared.Constants;

public static class FluentIcons
{
    public static readonly Icon Home = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Home();
    public static readonly Icon Tag = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Tag();
    public static readonly Icon ClipboardCheckmark = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.ClipboardCheckmark();
    public static readonly Icon People = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.People();
    public static readonly Icon ArrowExportUp = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.ArrowExportUp();
    public static readonly Icon ArrowClockwise = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.ArrowClockwise();
    public static readonly Icon TextBulletList = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.TextBulletList();
    public static readonly Icon Bot = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Bot();
    public static readonly Icon Add = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Add();
    public static readonly Icon PersonAdd = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.PersonAdd();
    public static readonly Icon Person = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Person();
    public static readonly Icon SlideText = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.SlideText();
    public static readonly Icon Book = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Book();
    public static readonly Icon Calendar = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Calendar();
    public static readonly Icon CheckmarkCircleFilled = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Filled.Size24.CheckmarkCircle();
    public static readonly Icon ArrowLeft = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.ArrowLeft();
    public static readonly Icon ArrowRight = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.ArrowRight();
    // Contact ordering (spec §4.9): move-up / move-down affordances.
    public static readonly Icon ChevronUp = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.ChevronUp();
    public static readonly Icon ChevronDown = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.ChevronDown();
    public static readonly Icon Save = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Save();
    public static readonly Icon Settings = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Settings();
    public static readonly Icon Open = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Open();
    public static readonly Icon Dismiss = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Dismiss();
    public static readonly Icon Edit = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Edit();

    // Phase 1 design-system hygiene: extend the curated set for shared
    // components (ContactsEditor) that need a non-generic Icon reference.
    // Each constant is the canonical icon the rest of the codebase uses
    // for the same semantic (Checkmark for "verified", Star for "primary",
    // Delete for "remove"). Curated constants are only safe with
    // `IconStart` / `IconStart` on `FluentButton`; the typed `Icon`
    // parameter on `<FluentIcon TIcon>` requires the generated
    // `Icons.Regular.Size{20,24}.<Name>()` type, which the Razor source
    // generator needs to resolve `nameof(FluentIcon<TIcon>.Icon)`.
    public static readonly Icon Checkmark = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Checkmark();
    public static readonly Icon Star = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Star();
    public static readonly Icon Delete = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Delete();
}
