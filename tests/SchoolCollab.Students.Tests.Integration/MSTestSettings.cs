[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace SchoolCollab.Students.Tests.Integration;

/// <summary>
/// Shared MSTest settings — method-level parallelization matches the other
/// integration test projects. The test class below opts out of parallelization
/// (it owns a single <see cref="ApiFactory"/> with its own Testcontainers
/// Postgres + RabbitMQ).
/// </summary>
internal static class MSTestSettings { }
