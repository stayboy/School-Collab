using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// Dialog data for creating or overriding a grade coded value.
/// </summary>
public sealed record GradeCodedValueDialogData(
    string Mode, // "Create" or "Override"
    Guid? ParentId, // Parent coded value ID (for Create mode)
    CodedValueDto? CodedValue, // For Override mode
    bool HasOverride = false // Whether this coded value already has an override
);

/// <summary>
/// Dialog result containing the created or updated coded value.
/// </summary>
public sealed record GradeCodedValueDialogResult(CodedValueDto CodedValue);