[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace SchoolCollab.Config.Tests.Integration;

/// <summary>
/// Shared MSTest settings — method-level parallelization matches the other
/// integration test projects. Each test class owns its own <see cref="ApiFactory"/>
/// (and thus its own Testcontainers Postgres + RabbitMQ) so tests never share state.
/// </summary>
internal static class MSTestSettings { }