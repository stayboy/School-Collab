using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Application;
using SchoolCollab.Core.Auth;
using SchoolCollab.Settings.Application;
using SchoolCollab.Students.Application;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Regression coverage for the unified Admin host failing at startup after Class A
/// (tenant propagation) landed. The host (<c>SchoolCollab.Admin/Program.cs</c>)
/// registers Settings, Assignments, and Students modules into <b>one</b> DI
/// container, in that order. All three now attach the SAME shared Core handler
/// <see cref="TenantPropagationDelegatingHandler"/> to their typed HttpClients.
/// The handler is stateless (reads the singleton <see cref="IDevTenantSelection"/>),
/// so one host-wide singleton is correct — but only if each module registers it
/// IDEMPOTENTLY.
///
/// <para>A regression to <c>AddSingleton</c> would throw <c>"type ... is already
/// registered"</c> the moment <c>AddAssignmentsModule()</c> runs after
/// <c>AddSettingsModule()</c>, crashing the host at
/// <c>builder.Services.AddStudentsModule()</c> and taking every page — including
/// the landing page and the Enroll Student dialog — down with it.</para>
///
/// <para>See docs/plans/2026-08-22-tenant-propagation-enroll-stream-investigation.md.</para>
/// </summary>
[TestClass]
public class TenantPropagationSharedHostRegistrationTests
{
    private sealed class StubSelection : IDevTenantSelection
    {
        private readonly Guid? _value;
        public StubSelection(Guid? value) => _value = value;
        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default) => Task.FromResult(_value);
        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Builds the Admin host DI in Program.cs order: Settings first, then
    /// Assignments, then Students — all three touching the same
    /// TenantPropagationDelegatingHandler type.</summary>
    private static ServiceProvider BuildUnifiedAdminContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton<IDevTenantSelection>(new StubSelection(null));
        services.AddSettingsModule();
        services.AddAssignmentsModule();
        services.AddStudentsModule();
        return services.BuildServiceProvider();
    }

    [TestMethod]
    public void UnifiedHost_Registers_Without_Collision()
    {
        // A regression to AddSingleton (a duplicate registration of the shared
        // Core handler type) is a no-op (last-wins), NOT a crash. The real
        // corruption was Singleton->InnerHandler overwrite across named clients;
        // the fix uses Transient so each named client pipeline gets its own instance.
        // This test just verifies the DI container builds and resolves the handler.
        var provider = BuildUnifiedAdminContainer();
        provider!.GetRequiredService<TenantPropagationDelegatingHandler>().Should().NotBeNull(
            "the shared handler must be resolvable so every module's AddHttpMessageHandler can attach it");
    }

    [TestMethod]
    public void Each_Resolution_Returns_A_Fresh_Instance()
    {
        // Because the handler is registered as TRANSIENT (not Singleton), every
        // call to GetRequiredService returns a new instance. This is critical:
        // IHttpClientFactory sets InnerHandler on each handler in the per-named-client
        // pipeline. A Singleton would get its InnerHandler overwritten when a
        // second named client's pipeline is built, corrupting the first client's
        // cached pipeline (calls to the wrong API host -> 404 -> blank page).
        var provider = BuildUnifiedAdminContainer();
        var a = provider.GetRequiredService<TenantPropagationDelegatingHandler>();
        var b = provider.GetRequiredService<TenantPropagationDelegatingHandler>();
        a.Should().NotBeSameAs(b,
            "the handler is registered as Transient so each named client's pipeline gets a distinct instance");
    }
}