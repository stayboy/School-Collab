using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.ApproveProvisionalCodedValue;

/// <summary>
/// System-wide approval that promotes a provisional (tenant-owned) coded value to
/// the shared global blueprint (tcv/3, spec §C). Only a provisional value may be
/// approved; approval clears the flag and moves <c>TenantId</c> to <see langword="null"/>.
/// </summary>
public sealed record ApproveProvisionalCodedValue(Guid Id) : ICommand;
