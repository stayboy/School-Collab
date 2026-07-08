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
}
