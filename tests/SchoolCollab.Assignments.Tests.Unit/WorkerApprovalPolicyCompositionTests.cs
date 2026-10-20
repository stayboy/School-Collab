using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.AutoReturnAssignmentCommand;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Http;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>assignment-approval-workflow</c> (WS4) — the Worker-side DI composition the approval
/// sweep depends on. The sweeper's own tests inject fakes, so a host that forgot one registration
/// would only fail at runtime, inside a BackgroundService, where nothing reports it. This test
/// composes the Worker's registration block (<c>AddAssignmentsCore</c> + the two named cross-module
/// clients + the relocated resolver — Assignments.Worker/Program.cs) and resolves every dependency
/// <c>ApprovalSweepService</c> asks for.
///
/// <para>The source-shape half — that Program.cs really contains those lines, in that order — is
/// <c>ApprovalPolicyResolverRelocationArchitectureTests</c> in the architecture suite.</para>
/// </summary>
[TestClass]
public class WorkerApprovalPolicyCompositionTests
{
    private static ServiceProvider BuildWorkerShapedContainer()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAssignmentsCore(configuration);
        services.AddCrossModuleHttpClient("settings-api", "https+http://settings-api", propagateTenant: false);
        services.AddCrossModuleHttpClient("students-api", "https+http://students-api", propagateTenant: false);
        // Assignments.Worker/Program.cs — registered AFTER AddAssignmentsCore, on the two named
        // clients above.
        services.AddScoped<IAssignmentPolicyResolver, AssignmentPolicyResolver>();
        return services.BuildServiceProvider();
    }

    [TestMethod]
    public void TheRelocatedResolver_ResolvesToTheCoreImplementation()
    {
        using var provider = BuildWorkerShapedContainer();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAssignmentPolicyResolver>()
            .Should().BeOfType<AssignmentPolicyResolver>(
                "one implementation in Assignments.Core, registered by BOTH hosts — the "
                + "NotificationPolicyResolver relocation precedent");
    }

    [TestMethod]
    public void EverySweepDependency_Resolves_InTheWorkerShapedContainer()
    {
        using var provider = BuildWorkerShapedContainer();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<IAssignmentRepository>().Should().NotBeNull();
        services.GetRequiredService<ICommandHandler<AutoReturnAssignmentCommand>>().Should().NotBeNull(
            "the auto-return handler is Scrutor-scanned from Assignments.Core — a Worker that lost "
            + "the scan would leave the sweep unable to act");
        services.GetRequiredService<ITenantContextAccessor>().Should().NotBeNull();
        services.GetRequiredService<TimeProvider>().Should().NotBeNull(
            "the sweep reads the clock through TimeProvider, never the wall clock");
        services.GetRequiredService<IAssignmentPolicyResolver>().Should().NotBeNull();
    }
}
