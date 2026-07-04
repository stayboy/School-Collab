using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Cross-cutting regression guard that catches the runtime error
/// <c>The model for context '&lt;T&gt;' has pending changes. Add a new
/// migration before updating the database.</c> at test time, before any
/// <c>dotnet ef database update</c> is run.
///
/// Per-domain <c>MigrationGuardTests</c> exist in the three
/// <c>&lt;Domain&gt;.Tests.Unit</c> projects. This central guard is
/// a belt-and-suspenders check that:
/// <list type="bullet">
///   <item>discovers every <c>IDesignTimeDbContextFactory&lt;TContext&gt;</c>
///         in the three <c>&lt;Domain&gt;.Core</c> assemblies, so a new
///         bounded context automatically gets covered;</item>
///   <item>invokes the <em>real</em> design-time factory rather than
///         hand-mirroring its options, so the test can never drift from
///         the factory callers of <c>dotnet ef</c> rely on;</item>
///   <item>saves and restores the per-context <see cref="OutboxMapping"/>
///         registry around each call so parallel tests cannot leak
///         flags into each other.</item>
/// </list>
/// </summary>
[TestClass]
public class MigrationGuardTests
{
    private static readonly Assembly StudentsCore =
        typeof(SchoolCollab.Students.Core.Data.StudentsDbContext).Assembly;

    private static readonly Assembly CodedValuesCore =
        typeof(SchoolCollab.Settings.Core.Data.SettingsDbContext).Assembly;

    private static readonly Assembly AssignmentsCore =
        typeof(SchoolCollab.Assignments.Core.Data.AssignmentsDbContext).Assembly;

    // CodedValues and Config merged into Settings (spec §3). The single
    // SettingsCore assembly replaces both CodedValuesCore and ConfigCore.
    private static readonly Assembly[] DomainCores = { StudentsCore, CodedValuesCore, AssignmentsCore };

    /// <summary>
    /// Pairs every <c>DbContext</c> in the three <c>&lt;Domain&gt;.Core</c>
    /// assemblies with its design-time factory, if one exists. Bounded
    /// contexts without a factory are ignored (they have no migrations).
    /// </summary>
    private static IReadOnlyList<(Type ContextType, IDesignTimeDbContextFactory<DbContext> Factory)>
        DiscoverDesignTimeFactories()
    {
        var pairs = new List<(Type, IDesignTimeDbContextFactory<DbContext>)>();

        foreach (var assembly in DomainCores)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (type is null || type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                var factoryInterface = type.GetInterfaces()
                    .FirstOrDefault(i =>
                        i.IsGenericType &&
                        i.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>));

                if (factoryInterface is null)
                {
                    continue;
                }

                var contextType = factoryInterface.GetGenericArguments()[0];
                if (!typeof(DbContext).IsAssignableFrom(contextType))
                {
                    continue;
                }

                // The IDesignTimeDbContextFactory<DbContext> parameterised
                // interface is the type-erased entry point we invoke;
                // the concrete impl is constructed by Activator.
                IDesignTimeDbContextFactory<DbContext> factory;
                try
                {
                    var instance = Activator.CreateInstance(type);
                    factory = (IDesignTimeDbContextFactory<DbContext>)instance!;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate design-time factory {type.FullName}: {ex.Message}", ex);
                }

                pairs.Add((contextType, factory));
            }
        }

        return pairs;
    }

    /// <summary>
    /// Snapshot of the <see cref="OutboxMapping"/> registry, so the
    /// test can restore any per-context outbox flags the design-time
    /// factory mutated during <c>CreateDbContext</c>. Without this,
    /// parallel test runs in the same AppDomain could leak the
    /// factory's flags into unrelated tests.
    /// </summary>
    private sealed record OutboxFlagsSnapshot(
        Type ContextType,
        OutboxConfigurationFlags Flags);

    /// <summary>
    /// Capture the current flags for every <see cref="DbContext"/> in
    /// the supplied set, restoring them in <see cref="TestCleanup"/>.
    /// </summary>
    [TestInitialize]
    public void CaptureOutboxFlags()
    {
        var snapshots = new List<OutboxFlagsSnapshot>();
        foreach (var (contextType, _) in DiscoverDesignTimeFactories())
        {
            var current = OutboxMapping.FlagsFor<DbContext>().GetType() == typeof(OutboxConfigurationFlags)
                ? ReadFlags(contextType)
                : OutboxConfigurationFlags.Default;
            snapshots.Add(new OutboxFlagsSnapshot(contextType, current));
        }
        _snapshots = snapshots;
    }

    private List<OutboxFlagsSnapshot> _snapshots = new();

    /// <summary>
    /// Restore the per-context outbox flags to the values captured in
    /// <see cref="CaptureOutboxFlags"/>, so a parallel test that runs
    /// after this one sees the registry as it was before.
    /// </summary>
    [TestCleanup]
    public void RestoreOutboxFlags()
    {
        foreach (var snapshot in _snapshots)
        {
            // Use reflection so we don't need a generic method per
            // context type; SetFlagsFor<TContext> is a public static.
            typeof(OutboxMapping)
                .GetMethod(nameof(OutboxMapping.SetFlagsFor))!
                .MakeGenericMethod(snapshot.ContextType)
                .Invoke(null, new object[] { snapshot.Flags });
        }
    }

    /// <summary>
    /// Read the current flags for the supplied <typeparamref name="TContext"/>
    /// via the public <see cref="OutboxMapping"/> façade.
    /// </summary>
    private static OutboxConfigurationFlags ReadFlags(Type contextType)
    {
        var method = typeof(OutboxMapping)
            .GetMethod(nameof(OutboxMapping.FlagsFor))!
            .MakeGenericMethod(contextType);
        return (OutboxConfigurationFlags)method.Invoke(null, Array.Empty<object>())!;
    }

    /// <summary>
    /// For every <c>IDesignTimeDbContextFactory&lt;TContext&gt;</c> in the
    /// three <c>&lt;Domain&gt;.Core</c> assemblies, build the context via
    /// its factory and assert
    /// <c>Database.HasPendingModelChanges() == false</c>.
    ///
    /// A failure means the runtime error
    /// <em>"The model for context '&lt;T&gt;' has pending changes.
    /// Add a new migration before updating the database."</em> would
    /// be raised by <c>dotnet ef database update</c>; the message
    /// includes the exact <c>dotnet ef migrations add</c> command
    /// needed to fix it.
    /// </summary>
    [TestMethod]
    public void NoDomainDbContext_HasPendingModelChanges()
    {
        // Arrange
        var pairs = DiscoverDesignTimeFactories();
        pairs.Should().NotBeEmpty(
            "Expected at least one IDesignTimeDbContextFactory<TContext> in " +
            "the <Domain>.Core assemblies. The guard relies on the design-time " +
            "factories to know the canonical per-context options and outbox flags.");

        var failures = new List<string>();

        foreach (var (contextType, factory) in pairs)
        {
            DbContext context;
            try
            {
                // The factory calls SetFlagsFor<TContext> internally as
                // needed, builds the context (which fires OnModelCreating),
                // and returns it. The test never touches a database.
                context = factory.CreateDbContext(Array.Empty<string>());
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"[{contextType.FullName}] design-time factory " +
                    $"{factory.GetType().FullName} threw " +
                    $"{ex.GetType().Name}: {ex.Message}");
                continue;
            }

            using (context)
            {
                var hasPending = context.Database.HasPendingModelChanges();
                if (hasPending)
                {
                    failures.Add(
                        $"[{contextType.FullName}] model has pending changes " +
                        "that are not reflected in a migration. Run " +
                        $"'dotnet ef migrations add <Name> --project " +
                        $"{contextType.Assembly.GetName().Name!.Replace(".Core", "")}' " +
                        "(see the per-domain MigrationGuardTests for the exact project path).");
                }
            }
        }

        // Assert
        failures.Should().BeEmpty(
            "Every <Domain>.Core DbContext must have its current model in " +
            "sync with the model snapshot. A pending change means the runtime " +
            "command 'dotnet ef database update' would fail with " +
            "\"The model for context ... has pending changes. Add a new " +
            "migration before updating the database.\" Run the listed " +
            "dotnet ef migrations add command, then re-run this test.\n\n" +
            string.Join("\n", failures));
    }

    /// <summary>
    /// Sanity check: the discovery logic must find at least the three
    /// known factories. If a future refactor renames them, this test
    /// fails fast with a clear message.
    /// </summary>
    [TestMethod]
    public void DiscoversAllKnownDesignTimeFactories()
    {
        var pairs = DiscoverDesignTimeFactories();
        var contextNames = pairs.Select(p => p.ContextType.Name).ToList();

        contextNames.Should().Contain("StudentsDbContext",
            "Students.Core must expose a design-time factory for EF migrations.");
        contextNames.Should().Contain("AssignmentsDbContext",
            "Assignments.Core must expose a design-time factory for EF migrations.");
        contextNames.Should().Contain("SettingsDbContext",
            "Settings.Core must expose a design-time factory for EF migrations.");
    }
}
