namespace SchoolCollab.Assignments.Core.Services.Delivery;

/// <summary>
/// SMTP transport settings bound from the <c>Smtp</c> configuration section
/// (AppHost <c>Parameters:</c> block → <c>Smtp__*</c> env vars on assignments-api;
/// see documents/configuration.md §2/§11).
///
/// <para><b>Presence of <see cref="Host"/> is the provider switch.</b> Blank/unset →
/// the null sender (dev/standalone default; nothing blows up without a mail server).
/// See <see cref="EmailSenderSelection"/>.</para>
/// </summary>
public sealed class SmtpOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Smtp";

    /// <summary>SMTP host. Blank ⇒ <c>NullEmailSender</c>.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP port (MailPit dev container default: 1025).</summary>
    public int Port { get; set; } = 1025;

    /// <summary>Optional SMTP user. Blank ⇒ anonymous.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Optional SMTP password (the AppHost <c>smtp-password</c> secret parameter).</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>The From address stamped on messages that do not carry their own.</summary>
    public string FromAddress { get; set; } = "no-reply@schoolcollab.local";

    /// <summary>Whether to negotiate STARTTLS (dev MailPit: false).</summary>
    public bool UseStartTls { get; set; }
}
