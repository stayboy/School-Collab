using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Admin.Shared.Components.Dialogs;

/// <summary>
/// <see cref="IDialogService"/> extension methods for the dialog shell.
/// </summary>
public static class DialogServiceExtensions
{
    /// <summary>
    /// Builds the <see cref="DialogOptions"/> every shell dialog uses: the
    /// caller's <paramref name="title"/> and a width derived from
    /// <paramref name="size"/>, plus the dismissal posture every existing call
    /// site had in v4 (<c>Modal = true</c> &rarr; v5 renders
    /// <c>type="alert"</c>, which blocks a stray backdrop click; and
    /// <c>PreventDismissOnEscape = true</c>, which blocks ESC) — an accidental
    /// overlay click or ESC key must never discard in-progress form state.
    ///
    /// <para>FluentUI's built-in footer actions are suppressed by leaving both
    /// labels unset — <see cref="DialogOptionsFooterAction.ToDisplay"/> is
    /// false for an action without a label, so no OK/Cancel row renders. The
    /// dialogs own their in-body Cancel/Save row via
    /// <see cref="DialogShellFooter"/>. (v5's
    /// <c>FluentDialogInstance.OnInitializedAsync</c> would otherwise fill
    /// those labels — <see cref="DialogShellBase{TModel, TResult}"/> clears
    /// them again.)</para>
    ///
    /// <para>The header close (X) is switched ON here:
    /// <see cref="DialogOptionsHeaderAction.Visible"/> defaults to
    /// <c>false</c>, so without this line the X never renders even once a
    /// dialog renders <see cref="DialogShellBody"/>. Its click is already wired
    /// by <c>FluentDialogInstance.OnInitializedAsync</c> (a
    /// <see cref="DialogShellBase{TModel, TResult}"/> maps it onto
    /// <c>HandleCancelAsync</c>; any other dialog closes as cancelled), so no
    /// per-dialog plumbing is added.</para>
    ///
    /// <para>The X also gets a <see cref="DialogOptionsHeaderAction.Title"/>:
    /// <c>FluentDialogBody</c> renders the header action as an icon-only
    /// <c>FluentButton</c> and the button binds its <c>aria-label</c> attribute
    /// to <c>Title</c> (there is no built-in localised default). Without a
    /// title the X renders unnamed — invisible to screen readers and
    /// unaddressable by an accessible-name selector.</para>
    ///
    /// <para>Exposed publicly so callers that need the
    /// <see cref="DialogOptions"/> directly (or tests) can build the same
    /// options without going through <see cref="ShowShellDialogAsync"/>.</para>
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="size">One of the four fixed <see cref="DialogWidth"/> widths. Default: <see cref="DialogWidth.Small"/> (420px).</param>
    /// <param name="height">Optional explicit dialog height (CSS value, e.g. <c>"72vh"</c>).
    /// Forwarded to <see cref="DialogOptions.Height"/>, which FluentUI 5.x applies to the
    /// dialog's own <c>::part(dialog)</c> element (v4 set a <c>--dialog-height</c> CSS var;
    /// v5 writes the style directly). Prefer a definite height on the dialog's
    /// <em>content root</em> (the <see cref="ReadOnlyDialogShell"/> pattern) — a box height
    /// alone does not grow the body, so the action row can be left stranded mid-dialog.
    /// Pass <c>null</c> (default) to keep FluentUI's own default.</param>
    public static DialogOptions BuildShellOptions(string title, DialogWidth size = DialogWidth.Small, string? height = null)
    {
        var options = new DialogOptions
        {
            Width = size.ToCssWidth(),
            Height = height,
            Modal = true,
            PreventDismissOnEscape = true,
        };
        options.Header.Title = title;
        options.Header.CloseAction.Visible = true;
        options.Header.CloseAction.Title = "Close";
        return options;
    }

    /// <summary>
    /// Shows a <see cref="DialogShellBase{TModel, TResult}"/>-derived dialog
    /// and returns the typed result, or <c>null</c> if the dialog was
    /// cancelled. Replaces the verbose
    /// <c>ShowDialogAsync / await dialog.Result / result.Data is XxxDialogResult</c>
    /// pattern duplicated across every call site today.
    /// </summary>
    /// <typeparam name="TComponent">The derived dialog component type. Must inherit <see cref="DialogShellBase{TModel, TResult}"/>.</typeparam>
    /// <typeparam name="TModel">The dialog's form-state type.</typeparam>
    /// <typeparam name="TResult">The success-payload type.</typeparam>
    /// <param name="model">The form model passed into the dialog via <see cref="DialogShellData{TModel}"/>, under the <c>Content</c> parameter.</param>
    /// <param name="title">Dialog title.</param>
    /// <param name="size">One of the four fixed <see cref="DialogWidth"/> widths. Default: <see cref="DialogWidth.Small"/> (420px).</param>
    /// <returns>The typed result on success; <c>null</c> if cancelled or if the result data is not a <see cref="DialogShellResult{TResult}"/>.</returns>
    public static async Task<TResult?> ShowShellDialogAsync<TComponent, TModel, TResult>(
        this IDialogService dialogService,
        TModel model,
        string title,
        DialogWidth size = DialogWidth.Small)
        where TComponent : ComponentBase
        where TModel : class
        where TResult : class
    {
        var options = BuildShellOptions(title, size);
        options.Parameters[nameof(DialogShellBase<TModel, TResult>.Content)] = new DialogShellData<TModel>(model);

        var result = await dialogService.ShowDialogAsync(typeof(TComponent), options);
        if (result.Cancelled) return null;
        return result.Value is DialogShellResult<TResult> r ? r.Value : null;
    }

    /// <summary>
    /// Shows a read-only dialog (a plain <see cref="ComponentBase"/> that is
    /// NOT a <see cref="DialogShellBase{TModel, TResult}"/>) with the same
    /// shell chrome every shell dialog uses (title + width + no
    /// built-in footer actions + blocked overlay/ESC dismissal).
    /// Used to open presentational components that have no model, no OK/Cancel
    /// result, and dismiss themselves via the cascading
    /// <see cref="IDialogInstance"/> reference (e.g. <c>GuardianContactsDialog</c>
    /// in the student view's "View all (N) contacts" anchor flow).
    /// </summary>
    /// <typeparam name="TComponent">The dialog content type. Must be a
    /// <see cref="ComponentBase"/>. FluentUI 5.x spreads
    /// <paramref name="parameters"/> onto the component's typed
    /// <c>[Parameter]</c> properties, so every key MUST match a
    /// <c>[Parameter]</c> property name (the dialogs declare their keys as
    /// <c>const string XxxKey = nameof(Xxx)</c> to keep that true by
    /// construction). See <c>documents/solution/dialog-parameter-binding.md</c>.
    /// </typeparam>
    /// <param name="title">Dialog title (rendered in the dialog header).</param>
    /// <param name="parameters">Content entries (name/value) spread onto the
    /// dialog's <c>[Parameter]</c> properties. Pass the dialog's published key
    /// constants (e.g. <c>StudentEditDialog.StudentIdKey</c>), NOT
    /// <c>nameof</c> — the constant is declared on the dialog, so a property rename
    /// updates the key and all callers follow; a bare <c>nameof</c> in the caller
    /// would silently stop matching.</param>
    /// <param name="size">One of the four fixed <see cref="DialogWidth"/>
    /// widths. Default <see cref="DialogWidth.Medium"/> (560px) — read-only
    /// dialogs typically carry more body than a 3-field form.</param>
    /// <param name="height">Optional explicit dialog height (CSS value). See
    /// <see cref="BuildShellOptions"/> for details.</param>
    /// <returns>The dialog's <see cref="DialogResult"/>, i.e. a task that
    /// completes when the dialog closes. Callers that do not care about the
    /// result can discard it (the dialog dismisses itself).</returns>
    public static async Task<DialogResult> ShowReadonlyDialogAsync<TComponent>(
        this IDialogService dialogService,
        string title,
        IDictionary<string, object?> parameters,
        DialogWidth size = DialogWidth.Medium,
        string? height = null)
        where TComponent : ComponentBase
    {
        var options = BuildShellOptions(title, size, height);
        foreach (var kvp in parameters)
        {
            options.Parameters[kvp.Key] = kvp.Value;
        }
        return await dialogService.ShowDialogAsync(typeof(TComponent), options);
    }

    /// <summary>
    /// Shows the reusable <see cref="ConfirmDialog"/> as a <b>modal</b>
    /// confirmation prompt and returns whether the user confirmed.
    ///
    /// <para>Unlike FluentUI's <c>ShowConfirmationAsync</c>/<c>ShowMessageBoxAsync</c>
    /// (which hide the dark overlay whenever a secondary action is defined), this
    /// always renders the modal overlay while still offering a Cancel button.
    /// In v5 the overlay-and-dismissibility pair is expressed by the modal
    /// <em>type</em>: <c>Modal = false</c> renders <c>type="modal"</c>, which
    /// keeps the native modal backdrop (inert background) <em>and</em> lets the
    /// user dismiss by clicking the overlay or pressing ESC — exactly the v4
    /// <c>Modal = true</c> + <c>PreventDismissOnOverlayClick = false</c>
    /// combination this method used to pass. (<c>Modal = true</c> would render
    /// <c>type="alert"</c>, whose backdrop click is swallowed — the posture the
    /// shell-form dialogs want, not a confirmation prompt.)</para>
    /// </summary>
    /// <param name="message">The confirmation message to display.</param>
    /// <param name="primaryText">Label for the confirm (primary) button.</param>
    /// <param name="secondaryText">Label for the cancel (secondary) button. Default: "Cancel".</param>
    /// <param name="title">Optional dialog title. Defaults to <paramref name="primaryText"/>.</param>
    /// <returns><c>true</c> when the user clicked the primary button; <c>false</c>
    /// when they cancelled, dismissed via the overlay, or pressed ESC.</returns>
    public static async Task<bool> ShowConfirmDialogAsync(
        this IDialogService dialogService,
        string message,
        string primaryText,
        string secondaryText = "Cancel",
        string? title = null)
    {
        var options = new DialogOptions
        {
            Width = "420px",
            Modal = false,
            PreventDismissOnEscape = false,
        };
        options.Header.Title = title ?? primaryText;
        options.Parameters[nameof(ConfirmDialog.Content)] =
            new ConfirmDialogContent(message, primaryText, secondaryText, title);

        var result = await dialogService.ShowDialogAsync(typeof(ConfirmDialog), options);
        return !result.Cancelled;
    }
}
