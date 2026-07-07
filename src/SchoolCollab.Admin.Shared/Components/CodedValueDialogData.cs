using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// Generic dialog data for creating a new coded value or overriding the display
/// name of an existing one. Used by the Grade-Level wizard for GRADE coded
/// values and (in the same wizard) for SUBJECT coded values when adding a new
/// subject to a grade.
/// </summary>
public sealed record CodedValueDialogData(
    string Mode, // "Create" or "Override"
    Guid? ParentId, // Parent coded value ID (for Create mode)
    CodedValueDto? CodedValue, // For Override mode
    bool HasOverride = false // Whether this coded value already has an override
);

/// <summary>
/// Dialog result containing the created or updated coded value.
/// </summary>
public sealed record CodedValueDialogResult(CodedValueDto CodedValue);
