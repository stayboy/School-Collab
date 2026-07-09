using FluentAssertions;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tests.Unit.Tenancy;

/// <summary>
/// Tests for <see cref="TenantContextAccessor"/> — the sanctioned tenant-filter /
/// save-guard bypass. Covers AC-4 (explicit-tenant save + restore) and EC-8
/// (nesting / unwind correctness). See global-tenant-filter.md §8.3 / NFR-4.
/// </summary>
[TestClass]
public class TenantContextAccessorTests
{
    [TestInitialize]
    public void Reset()
    {
        // Ensure no leftover suppression from a prior test in this async context.
        TenantContextAccessor.GuardSuppressed.Value = false;
    }

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TenantC = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [TestMethod]
    public async Task RunWithExplicitTenant_SetsTenantForCallback_RestoresOnExit()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));
        var accessor = new TenantContextAccessor(provider);

        provider.GetTenantContext().TenantId.Should().Be(TenantA);

        var result = await accessor.RunWithExplicitTenantAsync(TenantB, async ct =>
        {
            provider.GetTenantContext().TenantId.Should().Be(TenantB);
            return 42;
        }, TestContext.CancellationToken);

        result.Should().Be(42);
        provider.GetTenantContext().TenantId.Should().Be(TenantA, "the prior context is restored on exit");
    }

    [TestMethod]
    public async Task RunWithExplicitTenant_NullClearsTenant_RestoresOnExit()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));
        var accessor = new TenantContextAccessor(provider);

        await accessor.RunWithExplicitTenantAsync(null, async ct =>
        {
            provider.GetTenantContext().IsDefault.Should().BeTrue("null means no tenant (Guid.Empty)");
            return true;
        }, TestContext.CancellationToken);

        provider.GetTenantContext().TenantId.Should().Be(TenantA, "restored after a null-tenant scope");
    }

    [TestMethod]
    public async Task RunWithExplicitTenant_NestedScopes_UnwindCorrectly()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));
        var accessor = new TenantContextAccessor(provider);

        await accessor.RunWithExplicitTenantAsync(TenantB, async ct =>
        {
            provider.GetTenantContext().TenantId.Should().Be(TenantB);

            await accessor.RunWithExplicitTenantAsync(TenantC, async ct2 =>
            {
                provider.GetTenantContext().TenantId.Should().Be(TenantC);
                return true;
            }, TestContext.CancellationToken);

            provider.GetTenantContext().TenantId.Should().Be(TenantB, "inner scope restored to B");
            return true;
        }, TestContext.CancellationToken);

        provider.GetTenantContext().TenantId.Should().Be(TenantA, "outer scope restored to A");
    }

    [TestMethod]
    public async Task RunWithExplicitTenant_RestoresEvenWhenCallbackThrows()
    {
        var provider = new TenantProvider();
        provider.SetTenant(new TenantContext(TenantA, "A", TenantType.School));
        var accessor = new TenantContextAccessor(provider);

        var act = async () => await accessor.RunWithExplicitTenantAsync<bool>(TenantB, async ct =>
        {
            throw new InvalidOperationException("boom");
        }, TestContext.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        provider.GetTenantContext().TenantId.Should().Be(TenantA, "restored despite the throw");
    }

    [TestMethod]
    public void SuppressTenantGuard_ActiveInsideScope_RestoresOnDispose()
    {
        var accessor = new TenantContextAccessor(new TenantProvider());

        TenantContextAccessor.IsGuardSuppressed.Should().BeFalse();

        using (accessor.SuppressTenantGuard())
        {
            TenantContextAccessor.IsGuardSuppressed.Should().BeTrue();
        }

        TenantContextAccessor.IsGuardSuppressed.Should().BeFalse();
    }

    [TestMethod]
    public void SuppressTenantGuard_NestedScopes_UnwindCorrectly()
    {
        var accessor = new TenantContextAccessor(new TenantProvider());

        using (accessor.SuppressTenantGuard())
        {
            TenantContextAccessor.IsGuardSuppressed.Should().BeTrue();

            using (accessor.SuppressTenantGuard())
            {
                TenantContextAccessor.IsGuardSuppressed.Should().BeTrue();
            }

            TenantContextAccessor.IsGuardSuppressed.Should().BeTrue("inner scope restored to the outer's suppressed state");
        }

        TenantContextAccessor.IsGuardSuppressed.Should().BeFalse("outer scope restored to the default");
    }

    public TestContext TestContext { get; set; } = default!;
}
