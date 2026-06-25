namespace SchoolCollab.CodedValues.Admin.Components.Pages.CodedValues;

/// <summary>
/// Data record passed to <c>IDialogService.ShowDialogAsync</c> when hosting
/// <see cref="CodedValuesChat"/> inside a side-drawer <see cref="FluentDialog"/>.
/// The chat currently holds its conversation state in instance fields and does
/// not need any data passed at launch — this record exists purely so the
/// component can satisfy <c>IDialogContentComponent&lt;T&gt;</c>.
/// </summary>
public record CodedValuesChatData();