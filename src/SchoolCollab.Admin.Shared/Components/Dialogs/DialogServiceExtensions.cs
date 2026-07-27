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
    /// <c>IDialogContentComponent&lt;DialogParameters&gt;</c> (an empty
    /// marker interface — the component receives its entries as
    /// <c>[Parameter]</c> properties, no separate Content payload needed).
    /// </typeparam>
    /// <param name="title">Dialog title (rendered in the FluentDialog header).</param>
    /// <param name="parameters">Content parameters (key = parameter name,
    /// value = value). Pass <c>nameof(TComponent.MyProperty)</c> keys. Each
    /// entry is added to <see cref="DialogParameters"/> via its indexer;
    /// FluentUI binds indexer entries to the content component's
    /// <c>[Parameter]</c> properties.</param>
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
        // (Title/Width/etc.) and the content parameter entries. FluentUI
        // binds indexer entries to the content's [Parameter]s the same way
        // it binds the typed Title/Width/PrimaryAction/etc. properties.
        // The same instance is passed as both the TData content (the
        // dialog component's IDialogContentComponent.Content) and the
        // DialogParameters argument — the dialog ignores Content and
        // reads everything via [Parameter].
        var dialogParams = BuildShellParameters(title, size);
        foreach (var kvp in parameters)
        {
            dialogParams[kvp.Key] = kvp.Value;
        }
        return await dialogService.ShowDialogAsync<TComponent, DialogParameters>(dialogParams, dialogParams);
    }
}
