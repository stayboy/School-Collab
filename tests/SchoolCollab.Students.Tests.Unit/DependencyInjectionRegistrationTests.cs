using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using RabbitMQ.Client;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Core;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.RemoveTopicAssignment;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Regression tests for the DI wiring exposed by <see cref="Extensions.AddStudentsCore"/>.
/// These catch at build/test time the class of runtime failure seen when
/// <c>RemoveTopicAssignmentHandler</c> could not resolve its
/// <see cref="ITopicAssignmentRepository"/> dependency because the base repository
/// interface was never registered.
/// </summary>
[TestClass]
public class DependencyInjectionRegistrationTests
{
    /// <summary>
    /// Builds a provider from the same registrations <c>AddStudentsCore</c> applies,
    /// plus the cross-module ports the API host supplies in production but which
    /// <c>AddStudentsCore</c> itself does not register (so the container can actually
    /// resolve every handler). <see cref="ServiceProviderOptions.ValidateOnBuild"/>
    /// forces the container to construct every registered service eagerly, so a missing
    /// or unresolvable dependency fails the build instead of surfacing at request time.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Cross-module ports the API host supplies at startup (see Students.Api/Program.cs)
        // but which AddStudentsCore intentionally leaves for the host to wire up.
        services.AddSingleton<IActivityGroupAssignmentQuery>(Mock.Of<IActivityGroupAssignmentQuery>());
        services.AddSingleton<ICodedValuesApiClient>(Mock.Of<ICodedValuesApiClient>());
        services.AddSingleton<IEntityCodeGenerator>(Mock.Of<IEntityCodeGenerator>());
        // Use the real config-backed implementation (not a Moq proxy) because
        // IFeatureFlagService declares default interface methods Moq leaves unmocked.
        services.AddSingleton<IFeatureFlagService>(new ConfigurationFeatureFlagService(configuration));

        // RabbitMQ connection required by the outbox dispatcher background service that
        // AddOutbox registers. A stub is enough: the test never starts the hosted service.
        services.AddSingleton<IConnection>(Mock.Of<IConnection>());

        services.AddStudentsCore(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    /// <summary>
    /// Resolves every command/query handler in the Students.Core assembly through DI.
    /// If any handler depends on a service that was never registered (the failure mode
    /// that caused the RemoveTopicAssignment crash), the resolution throws and the test
    /// fails with the offending handler + interface named.
    /// </summary>
    [TestMethod]
    public void AllHandlers_ResolveFromContainer()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var handlerInterfaceDefinitions = new[]
        {
            typeof(ICommandHandler<>),
            typeof(ICommandHandler<,>),
            typeof(IQueryHandler<,>),
        };

        var assembly = typeof(Extensions).Assembly;
        var failures = new List<string>();
        var resolvedCount = 0;

        foreach (var handlerType in assembly.GetTypes())
        {
            if (handlerType.IsAbstract || handlerType.IsInterface || !handlerType.Name.EndsWith("Handler"))
                continue;

            foreach (var implemented in handlerType.GetInterfaces())
            {
                if (!implemented.IsGenericType ||
                    !handlerInterfaceDefinitions.Contains(implemented.GetGenericTypeDefinition()))
                {
                    continue;
                }

                var resolved = scope.ServiceProvider.GetService(implemented);
                if (resolved is null)
                {
                    failures.Add($"{handlerType.FullName} could not be resolved via {implemented.FullName}");
                }
                else
                {
                    resolvedCount++;
                }
            }
        }

        // Guard against the discovery silently matching nothing (which would make the
        // assertion above pass trivially). Every handler's service must actually activate.
        resolvedCount.Should().BeGreaterThan(0,
            "the scan must discover and resolve at least one handler service");

        failures.Should().BeEmpty(
            "every handler must be constructible from the DI container so missing " +
            "registrations (like ITopicAssignmentRepository) fail here, not at runtime.");
    }

    /// <summary>
    /// Focused regression test for the exact bug: the base <see cref="ITopicAssignmentRepository"/>
    /// must be resolvable and the handler depending on it must activate.
    /// </summary>
    [TestMethod]
    public void RemoveTopicAssignmentHandler_Resolves()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // The missing registration that caused the runtime crash.
        scope.ServiceProvider.GetRequiredService<ITopicAssignmentRepository>()
            .Should().NotBeNull("AddStudentsCore must register ITopicAssignmentRepository");

        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RemoveTopicAssignment>>();
        handler.Should().BeOfType<RemoveTopicAssignmentHandler>();
    }
}
