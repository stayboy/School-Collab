namespace SchoolCollab.Assignments.Core.Services.Delivery;

/// <summary>
/// A fully-rendered outbound SMS / WhatsApp message. The channel is carried by the
/// <c>NotificationLog</c> row, not the message, so one shape serves both.
/// </summary>
public sealed record SmsMessage(
    string To,
    string Body);
