namespace SchoolCollab.Admin.Shared.Components.Dialogs;

/// <summary>
/// Payload passed into a <see cref="DialogShellBase{TModel, TResult}"/>
/// dialog via FluentUI's <c>ShowDialogAsync&lt;TComponent, TData&gt;</c>.
/// The form model is opaque to the shell — the shell hands it to the
/// derived dialog's <c>SubmitAsync</c> hook and never inspects it.
/// </summary>
/// <typeparam name="TModel">The dialog's form-state record/class.</typeparam>
public sealed record DialogShellData<TModel>(TModel Model)
    where TModel : class;

/// <summary>
/// Wrapper the shell returns via <c>Dialog.CloseAsync</c>. Lets
/// <see cref="DialogServiceExtensions.ShowShellDialogAsync"/> unwrap the
/// success payload without the consumer type-testing
/// <c>result.Data is XxxDialogResult</c>.
/// </summary>
public sealed record DialogShellResult<TResult>(TResult Value)
    where TResult : class;
