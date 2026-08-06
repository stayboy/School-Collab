using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateProvisionalCodedValue;

/// <summary>
/// Creates a <b>tenant-owned, provisional</b> coded value (tcv/3) — used when an
/// override is impossible (e.g. Code AND Description both change) and a new value
/// must be created instead. The value is isolated to the current tenant and awaits
/// a system-wide approval (Settings admin) before becoming a shared global
/// blueprint.
/// </summary>
public sealed record CreateProvisionalCodedValue(
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    int DisplayOrder = 0) : ICommand;
