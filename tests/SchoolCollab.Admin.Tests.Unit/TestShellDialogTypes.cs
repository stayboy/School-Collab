namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Form-state model for <see cref="TestShellDialog"/>. Has no validation
/// attributes so the <c>EditForm</c>'s <c>OnValidSubmit</c> always fires
/// when the form is submitted in tests.
/// </summary>
public sealed class TestShellModel
{
    /// <summary>Controls what <see cref="TestShellDialog.SubmitAsync"/> does.</summary>
    public TestSubmitBehavior Behavior { get; set; } = TestSubmitBehavior.ReturnResult;

    /// <summary>The value wrapped in the returned <see cref="TestShellResult"/> on success.</summary>
    public string ResultValue { get; set; } = "ok";

    /// <summary>The message set on <see cref="DialogShellBase{TModel,TResult}.Error"/> / thrown, depending on <see cref="Behavior"/>.</summary>
    public string ErrorMessage { get; set; } = "boom";

    /// <summary>Bindable field (no validation) so the EditForm has something to bind.</summary>
    public string? Value { get; set; }
}

/// <summary>Controls the behaviour of <see cref="TestShellDialog.SubmitAsync"/>.</summary>
public enum TestSubmitBehavior
{
    /// <summary>Return a non-null <see cref="TestShellResult"/> (success path → dialog closes).</summary>
    ReturnResult,
    /// <summary>Return null and set <see cref="DialogShellBase{TModel,TResult}.Error"/> (stay-open path).</summary>
    ReturnNullWithError,
    /// <summary>Throw with <see cref="TestShellModel.ErrorMessage"/> (error-bar path → stay open).</summary>
    Throw,
}

/// <summary>The success-payload type returned by <see cref="TestShellDialog"/>.</summary>
public sealed record TestShellResult(string Value);

/// <summary>
/// Form-state model for <see cref="TestShellDialogAuto"/>. The dialog
/// self-closes on first render — either cancelling (AC-5) or closing with a
/// non-<see cref="DialogShellResult{TResult}"/> payload (AC-7).
/// </summary>
public sealed class TestShellAutoModel
{
    /// <summary>If true, the dialog calls <c>Dialog.CancelAsync()</c>; otherwise it closes with a foreign object.</summary>
    public bool Cancel { get; set; } = true;
}
