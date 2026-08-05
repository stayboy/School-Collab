namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>Dialog model for the student transfer (promote/demote) dialog.</summary>
public sealed record StudentTransferModel(Guid StudentId);

/// <summary>Result of the student transfer dialog: whether the transfer succeeded.</summary>
public sealed record StudentTransferResult(bool Success);
