using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Admin.Shared.Components.Dialogs;

/// <summary>
/// <see cref="IDialogService"/> extension methods for the dialog shell.
/// </summary>
public static class DialogServiceExtensions
{
    /// <summary>
    /// Builds the <see cref="DialogParameters"/> every shell dialog uses:
    /// the caller's <paramref name="title"/> and a width derived from
    /// <paramref name="size"/> plus the four constant fields
    /// (<c>PrimaryAction = null</c>, <c>SecondaryAction = null</c>,
    /// <c>PreventDismissOnOverlayClick = true</c>) that match what every
    /// existing call site passes today — the dialogs render their own
    /// in-body Cancel/Save buttons via <see cref="DialogShellFooter"/> and
    /// do not want FluentUI's built-in primary/secondary actions.
    ///
    /// <para>Exposed publicly so callers that need the <see cref="IDialogReference"/>
    /// directly (or tests) can build the same parameters without going through
    /// <see cref="ShowShellDialogAsync"/>.</para>
    ///
    /// <para><note type="note">FluentUI's <see cref="DialogParameters"/> stores its
    /// <c>Title</c>/<c>Width</c>/<c>PrimaryAction</c>/<c>SecondaryAction</c>/
    /// <c>PreventDismissOnOverlayClick</c> as ordinary settable properties (the
    /// dialog reads <c>Instance.Parameters.&lt;Property&gt;</c>, not the
    /// dictionary indexer), so this helper sets the properties directly via
    /// object-initializer syntax — the same pattern every existing call site
    /// uses.</note></para>
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="size">One of the four fixed <see cref="DialogSize"/> widths. Default: <see cref="DialogSize.Small"/> (420px).</param>
    public static DialogParameters BuildShellParameters(string title, DialogSize size = DialogSize.Small)
    {
        return new DialogParameters
        {
            Title = title,
            Width = size.ToCssWidth(),
            PrimaryAction = null,
            SecondaryAction = null,
            PreventDismissOnOverlayClick = true,
        };
    }

    /// <summary>
    /// Shows a <see cref="DialogShellBase{TModel, TResult}"/>-derived dialog
    /// and returns the typed result, or <c>null</c> if the dialog was
    /// cancelled. Replaces the verbose
    /// <c>ShowDialogAsync / await dialog.Result / result.Data is XxxDialogResult</c>
    /// pattern duplicated across every call site today.
    /// </summary>
    /// <typeparam name="TComponent">The derived dialog component type. Must inherit <see cref="DialogShellBase{TModel, TResult}"/> (which implements <c>IDialogContentComponent&lt;DialogShellData&lt;TModel&gt;&gt;</c>).</typeparam>
    /// <typeparam name="TModel">The dialog's form-state type.</typeparam>
    /// <typeparam name="TResult">The success-payload type.</typeparam>
    /// <param name="model">The form model passed into the dialog via <see cref="DialogShellData{TModel}"/>.</param>
    /// <param name="title">Dialog title.</param>
    /// <param name="size">One of the four fixed <see cref="DialogSize"/> widths. Default: <see cref="DialogSize.Small"/> (420px).</param>
    /// <returns>The typed result on success; <c>null</c> if cancelled or if the result data is not a <see cref="DialogShellResult{TResult}"/>.</returns>
    public static async Task<TResult?> ShowShellDialogAsync<TComponent, TModel, TResult>(
        this IDialogService dialogService,
        TModel model,
        string title,
        DialogSize size = DialogSize.Small)
        where TComponent : ComponentBase, IDialogContentComponent<DialogShellData<TModel>>
        where TModel : class
        where TResult : class
    {
        var parameters = BuildShellParameters(title, size);

        var dialog = await dialogService.ShowDialogAsync<TComponent, DialogShellData<TModel>>(
            new DialogShellData<TModel>(model), parameters);
        var result = await dialog.Result;
        if (result.Cancelled) return null;
        return result.Data is DialogShellResult<TResult> r ? r.Value : null;
    }

    /// <summary>
    /// Shows a read-only dialog (a plain <see cref="ComponentBase"/> that is
    /// NOT a <see cref="DialogShellBase{TModel, TResult}"/>) with the same
    /// shell chrome every shell dialog uses (title + width + no
    /// PrimaryAction/SecondaryAction + PreventDismissOnOverlayClick = true).
    /// Used to open presentational components that have no model, no OK/Cancel
    /// result, and dismiss themselves via the cascading
    /// <c>FluentDialog</c> reference (e.g. <c>GuardianContactsDialog</c> in
    /// the student view's "View all (N) contacts" anchor flow).
    /// </summary>
    /// <typeparam name="TComponent">The dialog content type. Must be a
    /// <see cref="ComponentBase"/> AND implement
    /// <c>IDialogContentComponent&lt;DialogParameters&gt;</c>. FluentUI renders the
    /// content via <c>DynamicComponent</c> with only
    /// <c>Parameters = { "Content": &lt;DialogParameters&gt; }</c> — it does NOT spread
    /// <c>DialogParameters</c> indexer entries onto the component's <c>[Parameter]</c>
    /// properties. So the component must read its inputs from
    /// <c>Content.TryGet&lt;T&gt;(XxxKey)</c>, not from separate <c>[Parameter]</c>s.
    /// See <c>documents/solution/dialog-parameter-binding.md</c>.
    /// </typeparam>
    /// <param name="title">Dialog title (rendered in the FluentDialog header).</param>
    /// <param name="parameters">Content entries (key/value) added to the
    /// <see cref="DialogParameters"/> indexer. Pass the dialog's published key
    /// constants (e.g. <c>StudentEditDialog.StudentIdKey</c>), NOT
    /// <c>nameof</c> — the constant is declared on the dialog, so a property rename
    /// updates the key and all callers follow; a bare <c>nameof</c> in the caller
    /// would silently stop matching. The dialog reads these back via
    /// <c>Content.TryGet&lt;T&gt;(key)</c> (FluentUI does NOT spread indexer entries
    /// to <c>[Parameter]</c>s).</param>
    /// <param name="size">One of the four fixed <see cref="DialogSize"/>
    /// widths. Default <see cref="DialogSize.Medium"/> (640px) — read-only
    /// dialogs typically carry more body than a 3-field form.</param>
    /// <returns>The <see cref="IDialogReference"/>. Callers may ignore it
    /// (the dialog dismisses itself) or await <c>dialog.Result</c>.</returns>
    public static async Task<IDialogReference> ShowReadonlyDialogAsync<TComponent>(
        this IDialogService dialogService,
        string title,
        IDictionary<string, object?> parameters,
        DialogSize size = DialogSize.Medium)
        where TComponent : ComponentBase, IDialogContentComponent<DialogParameters>
    {
        // Build a single DialogParameters carrying both the shell chrome
        // (Title/Width/etc.) and the content parameter entries. The same instance
        // is passed as both the TData content (IDialogContentComponent.Content)
        // and the DialogParameters argument — FluentUI only sets Content from the
        // TData; indexer entries are NOT spread to [Parameter] properties. The
        // dialog must read its inputs from Content.TryGet<T>(key), not from
        // separate [Parameter]s. See dialog-parameter-binding.md.
        var dialogParams = BuildShellParameters(title, size);
        foreach (var kvp in parameters)
        {
            dialogParams[kvp.Key] = kvp.Value;
        }
        return await dialogService.ShowDialogAsync<TComponent, DialogParameters>(dialogParams, dialogParams);
    }

    /// <summary>
    /// Shows the reusable <see cref="ConfirmDialog"/> as a <b>modal</b>
    /// confirmation prompt and returns whether the user confirmed.
    ///
    /// <para>Unlike FluentUI's <c>ShowConfirmationAsync</c>/<c>ShowMessageBoxAsync</c>
    /// (which hide the dark overlay whenever a secondary action is defined), this
    /// always renders the modal overlay (<c>Modal = true</c>) while still offering
    /// a Cancel button. <c>PreventDismissOnOverlayClick = false</c> (the default)
    /// lets the user dismiss by clicking the overlay or pressing ESC.</para>
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
        var dialog = await dialogService.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
            new ConfirmDialogContent(message, primaryText, secondaryText, title),
            new DialogParameters
            {
                Title = title ?? primaryText,
                // Null the default footer actions so FluentUI does NOT render its
                // own OK/Cancel buttons — the ConfirmDialog renders its own
                // Primary (Remove) / Secondary (Cancel) buttons.
                PrimaryAction = null,
                SecondaryAction = null,
                Modal = true,
                PreventDismissOnOverlayClick = false,
                Width = "420px",
            });
        var result = await dialog.Result;
        return !result.Cancelled;
    }
}
