namespace SchoolCollab.Assignments.Core.Services.Delivery;

/// <summary>The concrete email provider chosen for the current configuration.</summary>
public enum EmailProviderKind
{
    /// <summary>No SMTP host configured — log-and-skip (successful) sender.</summary>
    Null = 0,

    /// <summary>SMTP host configured — real MailKit delivery.</summary>
    MailKit = 1,
}

/// <summary>
/// Deterministic email-provider selection. Pure and static so both branches are
/// unit-testable without any transport, and so the DI registration in
/// <see cref="DeliveryServiceCollectionExtensions"/> performs the exact same choice.
///
/// <para>Rule: <c>Smtp:Host</c> set (non-blank) ⇒ <see cref="EmailProviderKind.MailKit"/>;
/// unset/blank ⇒ <see cref="EmailProviderKind.Null"/>. The null provider reports
/// <b>success</b> (decision (d)) — an unconfigured host must never fill the ar-17
/// failure surface.</para>
/// </summary>
public static class EmailSenderSelection
{
    /// <summary>Chooses the provider for the supplied options.</summary>
    public static EmailProviderKind Select(SmtpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrWhiteSpace(options.Host)
            ? EmailProviderKind.Null
            : EmailProviderKind.MailKit;
    }
}
