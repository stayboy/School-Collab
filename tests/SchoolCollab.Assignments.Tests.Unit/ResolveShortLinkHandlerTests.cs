using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ResolveShortLink;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round notification-templates-b2a (spec §3.6, Q61) — the public short-link resolution read.
/// The route is anonymous and serves a visitor with <b>no tenant at all</b>, so the assertions that
/// matter are: a code resolves to its token <b>across tenants</b> (it is the credential — the single
/// sanctioned <c>IgnoreQueryFilters(["Tenant"])</c> bypass), an expired link does not, and an unknown
/// code returns nothing. InMemory DbContext, because the tenant filter (not the index) is the
/// behaviour under test.
/// </summary>
[TestClass]
public class ResolveShortLinkHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid ContactId = Guid.Parse("00000000-0000-0000-0000-0000000000cc");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-0000000000dd");

    private const string Code = "AbCdEfGh12345678";

    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Builds the context under <paramref name="seedTenant"/> (where the row is written)
    /// and returns it together with a handler bound to the SAME context — the resolution runs with
    /// the seeding tenant still current, which is the harshest case for the bypass.</summary>
    private static async Task<(AssignmentsDbContext Db, ResolveShortLinkHandler Handler)> BuildAsync(
        Guid seedTenant, string? token, DateTimeOffset? expiresAt, bool attachCode = true)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        ((TenantProvider)provider.GetRequiredService<ITenantProvider>()).SetTenant(
            new TenantContext(seedTenant, "SchoolA", TenantType.School));
        await db.Database.EnsureCreatedAsync();

        var recipient = AssignmentRecipient.Create(
            seedTenant, AssignmentId, ContactOwnerType.Guardian, Guid.NewGuid(), StudentId,
            ContactId, ContactChannel.SMS, GuardianRole.Primary, notifyOnBroadcast: true, subscriptionActive: true);
        if (token is not null)
        {
            recipient.AttachDeepLink(token, expiresAt ?? Now.AddDays(7), attachCode ? Code : null);
        }

        db.AssignmentRecipients.Add(recipient);
        await db.SaveChangesAsync();

        var handler = new ResolveShortLinkHandler(db, new FixedClock(Now), NullLogger<ResolveShortLinkHandler>.Instance);
        return (db, handler);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [TestMethod]
    public async Task KnownCode_ResolvesToTheRecipientsDeepLinkToken()
    {
        var (db, handler) = await BuildAsync(TenantA, "token-abc", Now.AddDays(3));

        var result = await handler.HandleAsync(new ResolveShortLink(Code));

        result.Should().NotBeNull();
        result!.DeepLinkToken.Should().Be("token-abc");
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task CodeMintedForAnotherTenant_StillResolves()
    {
        // Discriminating for the Q61 posture: the code IS the credential, so the read cannot be
        // tenant-scoped — a tenant-filtered query would resolve nothing for a tenant-less visitor.
        var (db, handler) = await BuildAsync(TenantB, "token-from-tenant-b", Now.AddDays(3));

        var result = await handler.HandleAsync(new ResolveShortLink(Code));

        result!.DeepLinkToken.Should().Be("token-from-tenant-b");
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task ExpiredLink_DoesNotResolve()
    {
        var (db, handler) = await BuildAsync(TenantA, "token-expired", Now.AddDays(-1));

        (await handler.HandleAsync(new ResolveShortLink(Code))).Should().BeNull(
            "an expired code is the friendly expired page, not a live link");
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task UnknownCode_DoesNotResolve()
    {
        var (db, handler) = await BuildAsync(TenantA, "token-abc", Now.AddDays(3));

        (await handler.HandleAsync(new ResolveShortLink("ZzZzZzZz99999999"))).Should().BeNull();
        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task RecipientWithoutACode_NeverMatchesAnEmptyQuery()
    {
        // The email channel mints no code; an EMPTY code must never match those null rows.
        var (db, handler) = await BuildAsync(TenantA, "token-email", Now.AddDays(3), attachCode: false);

        (await handler.HandleAsync(new ResolveShortLink(string.Empty))).Should().BeNull();
        await db.DisposeAsync();
    }
}
