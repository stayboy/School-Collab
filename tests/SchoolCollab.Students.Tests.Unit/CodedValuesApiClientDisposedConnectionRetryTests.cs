using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Pins the disposed-connection self-healing on
/// <see cref="CodedValuesApiClient.GetByIdAsync"/>: the IHttpClientFactory
/// handler-lifetime rotation race hands requests a pooled connection whose
/// NetworkStream was just disposed — surfacing as
/// <c>ObjectDisposedException: 'System.Net.Sockets.NetworkStream'</c> from the
/// tenant delegating handler during enroll stream validation
/// (EnrollStudentHandler.ValidateStreamAsync). GET is idempotent, so the client
/// retries ONCE on a fresh request; standard resilience does not classify ODE
/// as retryable, which is why this must live in the client.
/// </summary>
[TestClass]
public class CodedValuesApiClientDisposedConnectionRetryTests
{
    private sealed class FlakyHandler : HttpMessageHandler
    {
        private int _calls;
        public int DisposeFailures { get; set; } = 1;
        public int CallCount => _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call <= DisposeFailures)
            {
                throw new ObjectDisposedException(
                    nameof(System.Net.Sockets.NetworkStream),
                    "Cannot access a disposed object. Object name: 'System.Net.Sockets.NetworkStream'.");
            }

            var dto = new StreamCodedValueDto(
                Id: Guid.Parse(request.RequestUri!.AbsolutePath.Split('/').Last()),
                Code: "GRSTREAMS_OK", Name: "Stream OK", Description: null,
                ParentId: null, ParentCode: "GRSTREAMS", IsDisabled: false, DisplayOrder: 1,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                new[] { new StreamAttributeDto("gradeLevel", Guid.NewGuid().ToString()) });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(dto), Encoding.UTF8, "application/json"),
            });
        }
    }

    private static CodedValuesApiClient NewClient(FlakyHandler handler, RecordingLogger logger) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://settings-api/") }, logger);

    private sealed class RecordingLogger : ILogger<CodedValuesApiClient>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [TestMethod]
    public async Task DisposedConnectionOnFirstAttempt_RetriesOnce_AndReturnsPayload()
    {
        var handler = new FlakyHandler { DisposeFailures = 1 };
        var logger = new RecordingLogger();
        var client = NewClient(handler, logger);

        var id = Guid.NewGuid();
        var result = await client.GetByIdAsync(id);

        result.Should().NotBeNull("one immediate retry heals the rotation race");
        result!.Id.Should().Be(id);
        handler.CallCount.Should().Be(2, "exactly one retry — no tight loop");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("retrying once"),
            "the healed failure must be visible to operators");
    }

    [TestMethod]
    public async Task DisposedConnectionOnBothAttempts_Propagates_AfterSingleRetry()
    {
        var handler = new FlakyHandler { DisposeFailures = 2 };
        var client = NewClient(handler, new RecordingLogger());

        var act = () => client.GetByIdAsync(Guid.NewGuid());

        (await act.Should().ThrowAsync<ObjectDisposedException>())
            .Which.ObjectName.Should().Contain("NetworkStream",
            "a persistent disposal is NOT masked — only transient artifacts are retried");
        handler.CallCount.Should().Be(2, "at most one retry — bounded");
    }

    [TestMethod]
    public async Task HealthyPipeline_MakesExactlyOneCall()
    {
        // Guard: the try/retry wrapper must not introduce extra calls when the
        // first attempt succeeds.
        var handler = new FlakyHandler { DisposeFailures = 0 };
        var client = NewClient(handler, new RecordingLogger());

        await client.GetByIdAsync(Guid.NewGuid());

        handler.CallCount.Should().Be(1);
    }
}
