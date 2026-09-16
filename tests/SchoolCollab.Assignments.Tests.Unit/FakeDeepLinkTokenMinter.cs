using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>Test double for <see cref="IDeepLinkTokenMinter"/> used by existing
/// handler tests that must keep compiling after the publish handler gained the
/// minter dependency but do not assert on token values. Returns the configured
/// <see cref="Token"/> and an expiry = <paramref name="now"/> + validity (or the
/// configured default) so downstream assertions on expiry deltas still hold.</summary>
public sealed class FakeDeepLinkTokenMinter : IDeepLinkTokenMinter
{
    /// <summary>The token string returned by every mint (defaults to a sentinel).</summary>
    public string Token { get; set; } = "fake-token";

    /// <summary>Fallback validity days when <c>linkValidityDays</c> is null.</summary>
    public int DefaultValidityDays { get; set; } = 7;

    public DeepLinkMintedToken Mint(AssignmentRecipient recipient, int? linkValidityDays, DateTimeOffset now)
        => new(Token, now.AddDays(linkValidityDays ?? DefaultValidityDays));
}
