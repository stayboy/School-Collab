namespace SchoolCollab.Assignments.Core.Services.Delivery;

/// <summary>
/// A fully-rendered outbound email. <see cref="From"/> is optional: when null the
/// sender falls back to its configured <c>Smtp:FromAddress</c>.
/// </summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string BodyHtml,
    string? From = null);
