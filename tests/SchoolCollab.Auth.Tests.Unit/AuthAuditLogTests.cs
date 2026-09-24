using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Auth.Services;

namespace SchoolCollab.Auth.Tests.Unit;

/// <summary>
/// Audit-log coverage (plan step 6 / AC-A-F): each mutation kind emits exactly one structured
/// record whose captured state names the actor, the action, the target and the tenant — and,
/// for role mutations, the role. The logger is a small recording <c>ILogger&lt;AuthAuditLog&gt;</c>
/// so the ASSERTIONS run over the real structured state, not over a formatted string.
/// </summary>
[TestClass]
public class AuthAuditLogTests
{
    private sealed class RecordingLogger : ILogger<AuthAuditLog>
    {
        public List<(LogLevel Level, IReadOnlyDictionary<string, object?> State)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var pairs = ((IEnumerable<KeyValuePair<string, object?>>)state!)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            Entries.Add((logLevel, pairs));
        }
    }

    private static (AuthAuditLog Log, RecordingLogger Logger) Create()
    {
        var logger = new RecordingLogger();
        return (new AuthAuditLog(logger), logger);
    }

    [TestMethod]
    public void UserCreated_EmitsOneInformationRecord_NamingActorActionTargetAndTenant()
    {
        var (log, logger) = Create();

        log.UserCreated(actor: "dev-teacher", targetUserId: "u1", tenantId: "t-1");

        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Information);
        entry.State["Actor"].Should().Be("dev-teacher");
        entry.State["Action"].Should().Be("user.created");
        entry.State["Target"].Should().Be("u1");
        entry.State["TenantId"].Should().Be("t-1");
    }

    [TestMethod]
    public void UserUpdated_EmitsTheUserUpdatedAction()
    {
        var (log, logger) = Create();

        log.UserUpdated(actor: "admin-1", targetUserId: "u1", tenantId: "t-1");

        logger.Entries.Should().ContainSingle().Which.State["Action"].Should().Be("user.updated");
    }

    [TestMethod]
    public void PasswordReset_EmitsThePasswordResetAction_WithNoPasswordInState()
    {
        var (log, logger) = Create();

        log.PasswordReset(actor: "admin-1", targetUserId: "u1", tenantId: "t-1");

        var state = logger.Entries.Should().ContainSingle().Which.State;
        state["Action"].Should().Be("user.password-reset");
        state.Keys.Should().NotContain(key => key.Contains("password", StringComparison.OrdinalIgnoreCase),
            "the audit record names the mutation kind, never the secret itself.");
    }

    [TestMethod]
    public void RoleAssigned_EmitsTheRoleAssignedAction_IncludingTheRoleName()
    {
        var (log, logger) = Create();

        log.RoleAssigned(actor: "admin-1", targetUserId: "u1", roleName: "user-admin", tenantId: "t-1");

        var state = logger.Entries.Should().ContainSingle().Which.State;
        state["Action"].Should().Be("role.assigned");
        state["Role"].Should().Be("user-admin");
    }

    [TestMethod]
    public void RoleUnassigned_EmitsTheRoleUnassignedAction_IncludingTheRoleName()
    {
        var (log, logger) = Create();

        log.RoleUnassigned(actor: "admin-1", targetUserId: "u1", roleName: "user-admin", tenantId: "t-1");

        var state = logger.Entries.Should().ContainSingle().Which.State;
        state["Action"].Should().Be("role.unassigned");
        state["Role"].Should().Be("user-admin");
    }
}
