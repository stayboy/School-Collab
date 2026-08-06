using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RejectProvisionalCodedValue;

/// <summary>
/// Rejects a provisional coded value (tcv/3, spec §C). It stays tenant-scoped
/// (isolated to its creating tenant — no hard delete) but leaves the pending
/// approval queue.
/// </summary>
public sealed record RejectProvisionalCodedValue(Guid Id) : ICommand;
