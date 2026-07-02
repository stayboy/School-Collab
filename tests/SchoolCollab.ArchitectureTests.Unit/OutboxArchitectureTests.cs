using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Architecture regression tests that enforce the audit checklist
/// from <c>documents/solution/shared-kernel-extraction-pattern.md</c>
/// §12. Each test maps directly to one bullet in that section.
///
/// The tests scan the compiled assemblies of the three
/// &lt;Domain&gt;.Core projects (Students, CodedValues, Assignments).
/// A passing test means the project complies with the audit
/// checklist; a failing test means the local outbox plumbing has
/// crept back in and should be removed in favour of the shared
/// kernel implementations.
/// </summary>
[TestClass]
public class OutboxArchitectureTests
{
    private static readonly Assembly StudentsCore =
        typeof(SchoolCollab.Students.Core.Data.StudentsDbContext).Assembly;

    private static readonly Assembly CodedValuesCore =
        typeof(SchoolCollab.CodedValues.Core.Data.CodedValuesDbContext).Assembly;

    private static readonly Assembly AssignmentsCore =
        typeof(SchoolCollab.Assignments.Core.Data.AssignmentsDbContext).Assembly;

    private static readonly Assembly ConfigCore =
        typeof(SchoolCollab.Config.Core.Data.ConfigDbContext).Assembly;

    private static readonly Assembly[] DomainCores =
    {
        StudentsCore,
        CodedValuesCore,
        AssignmentsCore,
        ConfigCore,
    };

    /// <summary>
    /// Format the failing types for human-readable error messages.
    /// </summary>
    private static string FailingTypesMessage(NetArchTest.Rules.TestResult result)
    {
        if (result.IsSuccessful) return string.Empty;
        var names = result.FailingTypes
            .Select(t => t.FullName)
            .Take(20);
        var suffix = result.FailingTypes.Count() > 20 ? "..." : string.Empty;
        return string.Join("\n", names) + suffix;
    }

    /// <summary>
    /// Bullet 1: no &lt;Domain&gt;.Core may declare a type in a
    /// <c>Messaging</c> namespace. The local <c>Messaging/</c>
    /// folder was removed by Phases 2 and 3 of the
    /// messaging-consolidation plan; if it reappears, the
    /// consolidated contract has been bypassed.
    /// </summary>
    [TestMethod]
    public void DomainCores_ShouldNotDeclareAnyMessagingNamespace()
    {
        // Arrange + Act
        var result = Types.InAssemblies(DomainCores)
            .ShouldNot()
            .ResideInNamespaceMatching(@"^SchoolCollab\.[^.]+\.Core\.Messaging(\.|$)")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            "Each <Domain>.Core project must not declare any types in " +
            "its own Messaging namespace. The shared kernel " +
            "(SchoolCollab.Core/Messaging/) is the only authorised " +
            "home for IIntegrationEventPublisher, OutboxMessage, " +
            "OutboxIntegrationEventPublisher<TContext>, " +
            "OutboxDispatcher<TContext>, and OutboxExtensions.\n\n" +
            "Failing types:\n" + FailingTypesMessage(result));
    }

    /// <summary>
    /// Bullet 2 (and similarly 3, 4): no &lt;Domain&gt;.Core may
    /// declare a type that implements <see cref="IIntegrationEventPublisher"/>.
    /// A local implementation here means a local
    /// <c>OutboxIntegrationEventPublisher</c> has been
    /// reintroduced.
    /// </summary>
    [TestMethod]
    public void DomainCores_ShouldNotDeclareLocalIIntegrationEventPublisherImpl()
    {
        // Arrange
        var sharedPublisher = typeof(IIntegrationEventPublisher);

        // Act
        var matchingTypes = Types.InAssemblies(DomainCores)
            .That()
            .ImplementInterface(sharedPublisher)
            .GetTypes();

        // Assert
        matchingTypes.Should().BeEmpty(
            "An IIntegrationEventPublisher implementation in a " +
            "<Domain>.Core project means a local " +
            "OutboxIntegrationEventPublisher has been reintroduced. " +
            "Use the shared OutboxIntegrationEventPublisher<TContext> " +
            "from SchoolCollab.Core/Messaging/ via " +
            "AddOutbox<TContext>(...).\n\n" +
            "Matching types:\n" +
            string.Join("\n", matchingTypes.Select(t => t.FullName)));
    }

    /// <summary>
    /// Bullet 4: no &lt;Domain&gt;.Core may declare a subclass of
    /// <see cref="OutboxMessage"/>. The local
    /// <c>Data/OutboxMessage.cs</c> was removed by Phase 3.
    /// </summary>
    [TestMethod]
    public void DomainCores_ShouldNotDeclareLocalOutboxMessageEntity()
    {
        // Arrange
        var sharedOutboxMessage = typeof(OutboxMessage);

        // Act
        var matchingTypes = Types.InAssemblies(DomainCores)
            .That()
            .Inherit(sharedOutboxMessage)
            .GetTypes();

        // Assert
        matchingTypes.Should().BeEmpty(
            "An OutboxMessage subclass in a <Domain>.Core project " +
            "means a local outbox row entity has been reintroduced. " +
            "The shared SchoolCollab.Core/Messaging/OutboxMessage is " +
            "the only authorised entity.\n\n" +
            "Matching types:\n" +
            string.Join("\n", matchingTypes.Select(t => t.FullName)));
    }

    /// <summary>
    /// Bullet 3: no &lt;Domain&gt;.Core may declare a
    /// <c>BackgroundService</c> subclass whose name ends with
    /// <c>Dispatcher</c>. The local <c>OutboxDispatcher</c> was
    /// removed by Phase 3.
    /// </summary>
    [TestMethod]
    public void DomainCores_ShouldNotDeclareLocalOutboxDispatcher()
    {
        // Arrange
        var backgroundService = typeof(Microsoft.Extensions.Hosting.BackgroundService);

        // Act
        var matchingTypes = Types.InAssemblies(DomainCores)
            .That()
            .Inherit(backgroundService)
            .And()
            .HaveNameEndingWith("Dispatcher")
            .GetTypes();

        // Assert
        matchingTypes.Should().BeEmpty(
            "A *Dispatcher BackgroundService in a <Domain>.Core " +
            "project means a local OutboxDispatcher has been " +
            "reintroduced. The shared OutboxDispatcher<TContext> from " +
            "SchoolCollab.Core/Messaging/ is the only authorised " +
            "implementation; AddOutbox<TContext>(...) wires it.\n\n" +
            "Matching types:\n" +
            string.Join("\n", matchingTypes.Select(t => t.FullName)));
    }

    /// <summary>
    /// Bullet 5: no &lt;Domain&gt;.Core may declare a subclass of
    /// <c>OutboxMessageConfiguration</c>. The local EF mapping was
    /// removed by the outbox-configuration consolidation plan.
    /// </summary>
    [TestMethod]
    public void DomainCores_ShouldNotDeclareLocalOutboxMessageConfiguration()
    {
        // Arrange
        var sharedConfiguration = typeof(SchoolCollab.Core.Data.Outbox.OutboxMessageConfiguration);

        // Act
        var matchingTypes = Types.InAssemblies(DomainCores)
            .That()
            .Inherit(sharedConfiguration)
            .GetTypes();

        // Assert
        matchingTypes.Should().BeEmpty(
            "An OutboxMessageConfiguration subclass in a <Domain>.Core " +
            "project means a local EF mapping has been reintroduced. " +
            "Each DbContext applies the shared OutboxMessageConfiguration " +
            "with its per-module OutboxConfigurationFlags (via " +
            "OutboxMapping.FlagsFor<TContext>()).\n\n" +
            "Matching types:\n" +
            string.Join("\n", matchingTypes.Select(t => t.FullName)));
    }

    /// <summary>
    /// Bullet 6: no &lt;Domain&gt;.Core type may depend on a type in
    /// the <c>RabbitMQ.Client</c> namespace. The shared kernel
    /// mediates RabbitMQ access via <c>OutboxDispatcher&lt;TContext&gt;</c>;
    /// &lt;Domain&gt;.Core projects only call
    /// <c>services.AddOutbox&lt;TContext&gt;(...)</c>.
    /// </summary>
    [TestMethod]
    public void DomainCores_ShouldNotDependOnRabbitMqClientNamespace()
    {
        // Arrange + Act
        // NetArchTest.eNhancedEdition treats HaveDependencyOnAny
        // positively; we run it and assert the matching list is
        // empty. We pass a single-element list to scan the
        // RabbitMQ.Client namespace for any dependencies.
        var dependentTypes = Types.InAssemblies(DomainCores)
            .That()
            .HaveDependencyOnAny("RabbitMQ.Client")
            .GetTypes()
            .ToList();

        // Assert
        dependentTypes.Should().BeEmpty(
            "Types in <Domain>.Core projects must not depend on the " +
            "RabbitMQ.Client namespace. The shared kernel mediates " +
            "RabbitMQ access via OutboxDispatcher<TContext>; " +
            "<Domain>.Core projects only call " +
            "services.AddOutbox<TContext>(...).\n\n" +
            "Types that depend on RabbitMQ.Client:\n" +
            string.Join("\n", dependentTypes.Select(t => t.FullName)));
    }
}