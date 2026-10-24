using Bunit;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using SchoolCollab.Admin.Shared.Components.Dialogs;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>One dialog a template page opened, captured from the mocked dialog service.</summary>
/// <param name="Dialog">The component type the page asked the shell to show.</param>
/// <param name="HeaderTitle">The dialog header — the sentence that should name the row by its title.</param>
/// <param name="Message">The confirmation text (adopt / publish), null for every other dialog.</param>
/// <param name="Prompt">The reason dialog's prompt above its input, null for every other dialog.</param>
internal sealed record TemplateDialogOpen(
    Type Dialog, string? HeaderTitle, string? Message, string? Prompt);

/// <summary>
/// The bUnit stand-in for the shared dialog service on the template surface (naming spec D11): it
/// records every dialog the page opens — so a test can assert <b>what</b> the page asked — and answers
/// the two the surface actually uses. The operator's answer is scripted before the click:
/// <see cref="ReasonAnswer"/> null means the reason dialog was dismissed, which must abandon the act.
/// <para>
/// The service is the concrete <see cref="DialogService"/> mocked and registered as
/// <see cref="IDialogService"/> (<see cref="FluentServiceMocks"/> records the v5 reason), and the
/// intercepted overload is the <c>(Type, DialogOptions)</c> one every
/// <see cref="DialogServiceExtensions"/> helper calls.
/// </para>
/// </summary>
internal sealed class TemplateDialogs
{
    private static readonly string ContentKey = nameof(DialogShellBase<object, object>.Content);

    /// <summary>Every dialog the page opened, in order.</summary>
    public List<TemplateDialogOpen> Opens { get; } = [];

    /// <summary>The reason the operator types into the shared reason dialog; null dismisses it.</summary>
    public string? ReasonAnswer { get; set; }

    /// <summary>Whether the adopt / publish confirmation is accepted.</summary>
    public bool ConfirmAnswer { get; set; }

    /// <summary>How many times the reason dialog was opened (save-edit and reset share it).</summary>
    public int ReasonOpenCount => Opens.Count(o => o.Dialog == typeof(ReasonDialog));

    /// <summary>The header of the most recent reason dialog — the line that names the row.</summary>
    public string? LastReasonTitle =>
        Opens.LastOrDefault(o => o.Dialog == typeof(ReasonDialog))?.HeaderTitle;

    /// <summary>The confirmation text of the most recent adopt / publish prompt.</summary>
    public string? LastConfirmMessage =>
        Opens.LastOrDefault(o => o.Dialog == typeof(ConfirmDialog))?.Message;

    /// <summary>Registers the mock and returns the recorder the test scripts.</summary>
    public static TemplateDialogs Register(BunitContext context)
    {
        var dialogs = new TemplateDialogs();
        context.RegisterDialogService()
            .Setup(d => d.ShowDialogAsync(It.IsAny<Type>(), It.IsAny<DialogOptions>()))
            .Returns((Type dialog, DialogOptions options) =>
                Task.FromResult(dialogs.NextResult(dialog, options)));
        return dialogs;
    }

    /// <summary>Records the open and answers it the way the operator just did.</summary>
    private DialogResult NextResult(Type dialog, DialogOptions options)
    {
        Opens.Add(new TemplateDialogOpen(
            dialog,
            options.Header.Title,
            ContentOf<ConfirmDialogContent>(options)?.Message,
            ContentOf<DialogShellData<ReasonDialog.ReasonFormModel>>(options)?.Model.Prompt));

        if (dialog == typeof(ReasonDialog))
        {
            return ReasonAnswer is { } reason
                ? DialogResult.Ok(new DialogShellResult<ReasonDialog.ReasonResult>(
                    new ReasonDialog.ReasonResult(reason)))
                : DialogResult.Cancel();
        }

        return ConfirmAnswer ? DialogResult.Ok(true) : DialogResult.Cancel();
    }

    /// <summary>The dialog's content model, spread onto <c>Content</c> by the shared helpers.</summary>
    private static TContent? ContentOf<TContent>(DialogOptions options)
        where TContent : class =>
        options.Parameters.TryGetValue(ContentKey, out var content) ? content as TContent : null;
}
