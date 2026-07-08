using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace SchoolCollab.Admin.Shared.Components.Dialogs;

/// <summary>
/// Base for "form" dialogs. Owns the shared logic that every
/// <c>IDialogContentComponent</c>-style dialog in the admin apps
/// duplicates today: the <c>Content</c> parameter, the
/// <c>FluentDialog</c> cascade, the <c>_saving</c>/<c>_error</c> state,
/// the submit&rarr;close plumbing, and the cancel plumbing.
///
/// <para>A derived dialog is a <c>.razor</c> file that does
/// <c>@inherits DialogShellBase&lt;TModel, TResult&gt;</c> and provides:</para>
/// <list type="bullet">
///   <item>its form fields in markup, bound to <see cref="Model"/>, inside an
///         <c>&lt;EditForm OnValidSubmit="HandleSubmitAsync"&gt;</c>;</item>
///   <item>a <c>&lt;DialogShellFooter&gt;</c> placed <em>inside</em> that
///         <c>EditForm</c> at the end of the markup, so the footer's
///         <c>Type="Submit"</c> button triggers the form's
///         <c>OnValidSubmit</c> (and thus validation);</item>
///   <item>an override of <see cref="SubmitAsync"/>: the side effect.
///         Return non-null to close the dialog with that result; return
///         null to keep the dialog open (the derived dialog is expected to
///         set <see cref="Error"/> in that case); throw to surface the
///         exception message in the error bar and keep the dialog open.</item>
/// </list>
///
/// <para>This class has <strong>no markup of its own</strong> — it is a
/// plain C# abstract class so it composes cleanly with FluentUI's
/// <c>ShowDialogAsync&lt;TComponent, TData&gt;</c> hosting model, which
/// renders <c>TComponent</c> as the dialog's entire content. The shared
/// markup (error message bar + Cancel/Save footer) lives in the
/// <see cref="DialogShellFooter"/> child component.</para>
///
/// <para>The <c>IDialogContentComponent&lt;DialogShellData&lt;TModel&gt;&gt;</c>
/// interface is implemented here (not re-declared via <c>@implements</c> in
/// the derived <c>.razor</c>) and inherited via standard C# inheritance.</para>
/// </summary>
/// <typeparam name="TModel">The dialog's form-state type (typically a <c>record</c> with a primary constructor). Supplied non-null via <see cref="Content"/> by <c>DialogServiceExtensions.ShowShellDialogAsync</c>; if absent, <see cref="Model"/> throws — that is a programming error, not a runtime condition.</typeparam>
/// <typeparam name="TResult">The success-payload type returned to the caller via <see cref="DialogShellResult{TResult}"/>.</typeparam>
public abstract class DialogShellBase<TModel, TResult>
    : ComponentBase, IDialogContentComponent<DialogShellData<TModel>>
    where TModel : class
    where TResult : class
{
    /// <summary>
    /// The dialog payload supplied by
    /// <c>DialogServiceExtensions.ShowShellDialogAsync</c>. Wraps the
    /// derived dialog's form model.
    /// </summary>
    [Parameter]
    public DialogShellData<TModel> Content { get; set; } = default!;

    /// <summary>The FluentUI dialog that hosts this component.</summary>
    [CascadingParameter]
    public FluentDialog Dialog { get; set; } = default!;

    private TModel? _model;
    private bool _saving;
    private string? _error;

    /// <summary>
    /// The form model. Resolved from <see cref="Content"/>. Always supplied
    /// non-null by <c>DialogServiceExtensions.ShowShellDialogAsync</c>; if a
    /// caller bypasses the extension and passes a null model, accessing this
    /// throws <see cref="InvalidOperationException"/> — that is a
    /// programming error, not a runtime condition the shell recovers from.
    /// </summary>
    protected TModel Model => _model ??= Content?.Model ?? throw new InvalidOperationException(
        $"{GetType().Name}: Content.Model was not supplied. Use DialogServiceExtensions.ShowShellDialogAsync to open the dialog.");

    /// <summary>True while <see cref="SubmitAsync"/> is in flight; disables the footer buttons.</summary>
    protected bool Saving => _saving;

    /// <summary>
    /// The current error message, rendered by <see cref="DialogShellFooter"/>.
    /// Derived dialogs may set this directly on the null-return path of
    /// <see cref="SubmitAsync"/>; throwing from <see cref="SubmitAsync"/>
    /// also sets it (to <c>ex.Message</c>).
    /// </summary>
    protected string? Error
    {
        get => _error;
        set => _error = value;
    }

    /// <summary>Label on the Submit button. Default: "Save".</summary>
    protected virtual string SubmitText => "Save";

    /// <summary>Label shown on the Submit button while <see cref="Saving"/> is true. Default: "Saving...". Override to preserve a dialog-specific verb (e.g. "Creating...").</summary>
    protected virtual string SavingText => "Saving...";

    /// <summary>
    /// Hook for derived dialogs to hydrate the model from typed content
    /// (e.g. <c>CodedValueDialog</c> reads <c>Model.CodedValue</c> /
    /// <c>Model.HasOverride</c> here). Default: no-op.
    /// </summary>
    protected virtual void OnModelInitialized(TModel model) { }

    /// <summary>
    /// Submits the form. Invoked by <see cref="HandleSubmitAsync"/> after
    /// the <c>EditForm</c> reports a valid submit.
    /// <para>Return non-null to close the dialog with that result (wrapped in
    /// <see cref="DialogShellResult{TResult}"/>); return null to keep the
    /// dialog open (set <see cref="Error"/> first); throw to surface the
    /// exception message and keep the dialog open.</para>
    /// </summary>
    protected abstract Task<TResult?> SubmitAsync(TModel model);

    protected override void OnInitialized() => OnModelInitialized(Model);

    /// <summary>
    /// The <c>EditForm.OnValidSubmit</c> handler. Guards against
    /// double-submit (EC-1), flips <see cref="Saving"/>, calls
    /// <see cref="SubmitAsync"/>, and closes the dialog on a non-null
    /// result. Exceptions are surfaced via <see cref="Error"/>.
    /// </summary>
    protected async Task HandleSubmitAsync()
    {
        if (_saving) return; // EC-1: double-submit guard
        _saving = true;
        _error = null;
        try
        {
            var result = await SubmitAsync(Model);
            if (result is not null)
            {
                await Dialog.CloseAsync(new DialogShellResult<TResult>(result));
            }
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _saving = false;
        }
    }

    /// <summary>The Cancel button handler. Delegates to <c>Dialog.CancelAsync</c>.</summary>
    protected Task HandleCancelAsync() => Dialog.CancelAsync();
}
