namespace SchoolCollab.Settings.Core.Domain.Events;

/// <summary>
/// Raised when a provisional (tenant-created) coded value is promoted to the
/// shared global blueprint by a system-wide approval (tcv/3). The value's
/// <c>TenantId</c> moves from a real tenant to <see langword="null"/>.
/// </summary>
public record CodedValueApprovedEvent(Guid Id, string Code, string Name, Guid? ParentId) : IDomainEvent;
