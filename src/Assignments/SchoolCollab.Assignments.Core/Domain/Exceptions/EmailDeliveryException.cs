namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// Raised when an email cannot be delivered through SMTP (connect / authenticate /
/// send failure). Typed so the dispatcher can record a durable failure reason and
/// schedule a retry — never a bare <see cref="Exception"/> (dotnet-best-practices:
/// typed domain exceptions only, no <see cref="InvalidOperationException"/> from new code).
/// </summary>
public sealed class EmailDeliveryException : Exception
{
    /// <summary>Creates the exception with a human-readable reason.</summary>
    public EmailDeliveryException(string message) : base(message) { }

    /// <summary>Creates the exception, preserving the transport-level cause.</summary>
    public EmailDeliveryException(string message, Exception innerException)
        : base(message, innerException) { }
}
