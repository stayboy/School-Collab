namespace SchoolCollab.Students.Core.Domain.Events;

public sealed record SubjectDeletedEvent(Guid Id, string Code) : IDomainEvent;