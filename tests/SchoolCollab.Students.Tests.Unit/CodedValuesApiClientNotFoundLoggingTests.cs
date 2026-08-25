using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="CodedValuesApiClient.GetByIdAsync"/> — the
/// students-api → settings-api mid-flight hop used by enroll-time grade
/// materialization and stream validation.
///
/// <para>Pinned contract: a 404 is treated as "coded value not found" (the
/// caller maps null → <c>GradeLevelNotFoundException</c>), BUT the hop now
/// LOGS A WARNING naming the base address. Rationale: this exact hop once
/// returned 404s from the WRONG host entirely (typed-client InnerHandler
/// corruption), which was indistinguishable from a genuine miss and got
/// misdiagnosed as bad data. The warning makes routing corruption visible.</para>
/// </summary>
[TestClass]
public class CodedValuesApiClientNotFoundLoggingTests
{
    /// <summary>Minimal capturing ILogger so the warning can be asserted
    /// without pulling in a logging-test package.</summary>
    private sealed class RecordingLogger : ILogger<CodedValuesApiClient>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class StubHttpHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    [TestMethod]
    public async Task NotFound_ReturnsNull_AndLogsWarningNamingTheHop()
    {
        var handler = new StubHttpHandler(HttpStatusCode.NotFound);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://settings-api/") };
        var logger = new RecordingLogger();
        var client = new CodedValuesApiClient(http, logger);

        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var result = await client.GetByIdAsync(id);

        result.Should().BeNull("a 404 maps to 'coded value not found' (caller throws GradeLevelNotFoundException)");

        var warning = logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning,
            "the misrouting-ambiguity warning is the whole point of this change").Subject;
        warning.Message.Should().Contain(id.ToString(), "the log names the coded value that was requested");
        warning.Message.Should().Contain("settings-api", "the log names the BASE ADDRESS so a wrong-host 404 is diagnosable from the operator log alone");
        handler.LastRequestUri!.AbsolutePath.Should().Be($"/api/coded-values/{id}");
    }

    [TestMethod]
    public async Task NotFound_WithNullLogger_DoesNotThrow()
    {
        // The registration passes whatever ILogger<CodedValuesApiClient> DI
        // resolves; the optional-parameter shape must tolerate a null logger.
        var handler = new StubHttpHandler(HttpStatusCode.NotFound);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://settings-api/") };
        var client = new CodedValuesApiClient(http, logger: null);

        var act = () => client.GetByIdAsync(Guid.NewGuid());

        (await act.Should().NotThrowAsync()).Which.Should().BeNull();
    }
}
