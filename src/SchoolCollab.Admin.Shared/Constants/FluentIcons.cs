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
    public static readonly Icon Document = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Document();
    public static readonly Icon Book = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Book();
    public static readonly Icon Calendar = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Calendar();
    public static readonly Icon CheckmarkCircleFilled = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Filled.Size24.CheckmarkCircle();
    public static readonly Icon ArrowLeft = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.ArrowLeft();
    public static readonly Icon ArrowRight = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.ArrowRight();
    // Contact ordering (spec §4.9): move-up / move-down affordances.
    public static readonly Icon ChevronUp = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.ChevronUp();
    public static readonly Icon ChevronDown = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.ChevronDown();
    // PanelSection's opt-in collapse chevron, collapsed state (panel-layout-pattern §6).
    public static readonly Icon ChevronRight = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.ChevronRight();
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

    /// <summary>Vertical "kebab" (⋮) icon — the standard overflow / row-actions
    /// trigger. Used by <see cref="Components.RowActionsMenu"/>.</summary>
    public static readonly Icon MoreVertical = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.MoreVertical();

    /// <summary>Copy-semantic icon for the duplicate-as-template action
    /// (WS-A4 / spec §3.1). Plain <c>Copy</c> does not exist in the FluentUI
    /// 4.14.2 icon set; <c>DocumentCopy</c> is the copy-semantic icon that
    /// does.</summary>
    public static readonly Icon DocumentCopy = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.DocumentCopy();

    /// <summary>D11/D13: the small inline help-hint glyph rendered by
    /// <see cref="Components.HelpIcon"/>. Size16 — it sits INSIDE a form row's label or a
    /// card header, not on a button, so the 20px set would be oversized.</summary>
    public static readonly Icon Info = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size16.Info();

    /// <summary>D13: the padlock hint for "this value is inherited from grade/tenant policy"
    /// — used on the Rules card's header and on each of its readout subitems.</summary>
    public static readonly Icon LockClosed = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size16.LockClosed();

    // ── Instruction/material KIND glyphs (round material-row-editing, grill Q1/Q7) ──────────────
    //
    // The shared instruction editor names a row's kind with an ICON instead of the word, so these five
    // are the closed set that indicator can render — one per InstructionKindDto. Text and Link
    // deliberately reuse the authoring kebab's own two glyphs (it labels its actions "Add Text Content"
    // and "Add Link"), because the menu that creates a row and the row itself must not drift. Size16,
    // like Info/LockClosed: these sit inline in a list row, not on a button.

    /// <summary>A Text material — the same glyph the kebab's "Add Text Content" action carries.</summary>
    public static readonly Icon TextDescription = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size16.TextDescription();

    /// <summary>A Link material — the same glyph the kebab's "Add Link" action carries.</summary>
    public static readonly Icon Link = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size16.Link();

    /// <summary>An Audio material.</summary>
    public static readonly Icon MusicNote2 = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size16.MusicNote2();

    /// <summary>A Video material.</summary>
    public static readonly Icon Video = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size16.Video();

    /// <summary>An Image material.</summary>
    public static readonly Icon Image = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size16.Image();

    // ── Recording affordances (round media-item pill polish, 2026-10-12) ──────────────────────────

    /// <summary>The microphone glyph for AUDIO record affordances (the media row's Record button, the
    /// recorder's Start button) — <see cref="MusicNote2"/> names a staged audio FILE; this one says
    /// "speak into the mic now". Size20, like the other button glyphs.</summary>
    public static readonly Icon Mic = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Mic();

    /// <summary>The eye glyph for the media-preview toggle in the material metadata dialog — "show me
    /// the file" (round <c>media-pill-listing</c> post-close rework, owner 2026-10-14). Size20, like
    /// the other button glyphs.</summary>
    public static readonly Icon Eye = new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size20.Eye();
    /// <summary>A late/due/overdue signal — the Overdue notification kind''s glyph.</summary>
    public static readonly Icon Warning =
        new global::Microsoft.FluentUI.AspNetCore.Components.Icons.Regular.Size16.Warning();
}
