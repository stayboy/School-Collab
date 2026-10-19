using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round notification-templates-b2a (spec §3.6) — the link the renderer embeds, as a
/// <b>channel × base-URL × code</b> matrix. The choice is channel-driven: a plain-text recipient
/// with a minted code on a configured public base gets the short link <c>{base}/l/{code}</c>;
/// every other combination keeps the full <c>/deeplink/{token}</c>, relative when no base is
/// configured (the fail-closed dev posture §3.2/§3.5).
/// </summary>
[TestClass]
public class NotificationLinkOptionsTests
{
    private const string BaseUrl = "https://families.example.com";
    private const string Token = "tok-1";
    private const string Code = "AbCdEfGh12345678";

    private static NotificationLinkOptions Options(string? publicBaseUrl = null) =>
        new() { PublicBaseUrl = publicBaseUrl };

    // ── no base URL configured: relative, and never a short link (an unset base cannot address
    //    the Families /l route) ──

    [TestMethod]
    public void NoBase_Email_IsRelativeDeepLink()
    {
        Options().BuildDeepLink(Token, NotificationChannel.Email, Code)
            .Should().Be("/deeplink/tok-1");
    }

    [TestMethod]
    public void NoBase_SmsWithCode_IsStillRelativeDeepLink()
    {
        // Fail-closed posture: without a base URL the short form would be an unprefixed "/l/{code}"
        // the SMS cannot resolve from outside the app — so the relative full link stands.
        Options().BuildDeepLink(Token, NotificationChannel.SMS, Code)
            .Should().Be("/deeplink/tok-1");
    }

    // ── base URL configured: absolute; short only for the plain-text channels ──

    [TestMethod]
    public void Base_Email_IsAbsoluteDeepLink_EvenWithACode()
    {
        // Email keeps the full token link (§3.6): a code is only ever minted/used for SMS/WhatsApp.
        Options(BaseUrl).BuildDeepLink(Token, NotificationChannel.Email, Code)
            .Should().Be("https://families.example.com/deeplink/tok-1");
    }

    [TestMethod]
    public void Base_SmsWithCode_IsTheShortLink()
    {
        Options(BaseUrl).BuildDeepLink(Token, NotificationChannel.SMS, Code)
            .Should().Be($"https://families.example.com/l/{Code}");
    }

    [TestMethod]
    public void Base_WhatsAppWithCode_IsTheShortLink()
    {
        Options(BaseUrl).BuildDeepLink(Token, NotificationChannel.WhatsApp, Code)
            .Should().Be($"https://families.example.com/l/{Code}");
    }

    [TestMethod]
    public void Base_SmsWithoutCode_IsAbsoluteDeepLink()
    {
        // A plain-text recipient whose code could not be minted (row missing) degrades to the full
        // link rather than rendering a "/l/" with an empty code.
        Options(BaseUrl).BuildDeepLink(Token, NotificationChannel.SMS, shortLinkCode: null)
            .Should().Be("https://families.example.com/deeplink/tok-1");
    }

    // ── §3.5 base-URL normalisation ──

    [TestMethod]
    public void TrailingSlashOnTheBase_IsNormalised()
    {
        Options("https://families.example.com/").BuildDeepLink(Token, NotificationChannel.Email, null)
            .Should().Be("https://families.example.com/deeplink/tok-1");

        Options("https://families.example.com/").BuildDeepLink(Token, NotificationChannel.SMS, Code)
            .Should().Be($"https://families.example.com/l/{Code}");
    }

    [TestMethod]
    public void WhitespaceBase_CountsAsUnset_SoTheDevPostureHolds()
    {
        Options("   ").BuildDeepLink(Token, NotificationChannel.SMS, Code)
            .Should().Be("/deeplink/tok-1");
    }
}
