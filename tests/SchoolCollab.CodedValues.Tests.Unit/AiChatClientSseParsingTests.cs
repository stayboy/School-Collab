using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.AI;

namespace SchoolCollab.CodedValues.Tests.Unit;

[TestClass]
public class AiChatClientSseParsingTests
{
    [TestMethod]
    public void ParseSseEvent_TextChunk_CamelCase_DeserializesText()
    {
        // Server sends camelCase: {"text":"Hello!"}
        var data = """{"text":"Hello!"}""";
        var result = AiChatClient.ParseSseEvent("TextChunk", data);

        result.Should().BeOfType<ChatUpdate.TextChunk>();
        var chunk = (ChatUpdate.TextChunk)result!;
        chunk.Text.Should().Be("Hello!");
    }

    [TestMethod]
    public void ParseSseEvent_TextChunk_PascalCase_AlsoDeserializes()
    {
        // Ensure PascalCase still works (PropertyNameCaseInsensitive handles both)
        var data = """{"Text":"Hello!"}""";
        var result = AiChatClient.ParseSseEvent("TextChunk", data);

        result.Should().BeOfType<ChatUpdate.TextChunk>();
        var chunk = (ChatUpdate.TextChunk)result!;
        chunk.Text.Should().Be("Hello!");
    }

    [TestMethod]
    public void ParseSseEvent_TextChunk_NullText_YieldsEmptyString()
    {
        var data = """{"text":null}""";
        var result = AiChatClient.ParseSseEvent("TextChunk", data);

        result.Should().BeOfType<ChatUpdate.TextChunk>();
        var chunk = (ChatUpdate.TextChunk)result!;
        chunk.Text.Should().BeEmpty();
    }

    [TestMethod]
    public void ParseSseEvent_TextChunk_MissingTextProperty_YieldsEmptyString()
    {
        var data = """{"other":"value"}""";
        var result = AiChatClient.ParseSseEvent("TextChunk", data);

        result.Should().BeOfType<ChatUpdate.TextChunk>();
        var chunk = (ChatUpdate.TextChunk)result!;
        chunk.Text.Should().BeEmpty();
    }

    [TestMethod]
    public void ParseSseEvent_ToolCallStart_CamelCase_DeserializesAllFields()
    {
        var data = """{"callId":"call_1","friendlyName":"List Categories","argsSummary":"parentCode=HSPTL"}""";
        var result = AiChatClient.ParseSseEvent("ToolCallStart", data);

        result.Should().BeOfType<ChatUpdate.ToolCallStart>();
        var tc = (ChatUpdate.ToolCallStart)result!;
        tc.CallId.Should().Be("call_1");
        tc.FriendlyName.Should().Be("List Categories");
        tc.ArgsSummary.Should().Be("parentCode=HSPTL");
    }

    [TestMethod]
    public void ParseSseEvent_ToolCallStart_NullFields_YieldDefaults()
    {
        var data = """{"callId":null,"friendlyName":null,"argsSummary":null}""";
        var result = AiChatClient.ParseSseEvent("ToolCallStart", data);

        result.Should().BeOfType<ChatUpdate.ToolCallStart>();
        var tc = (ChatUpdate.ToolCallStart)result!;
        tc.CallId.Should().BeEmpty();
        tc.FriendlyName.Should().BeEmpty();
        tc.ArgsSummary.Should().BeEmpty();
    }

    [TestMethod]
    public void ParseSseEvent_ToolCallEnd_CamelCase_DeserializesAllFields()
    {
        var data = """{"callId":"call_1","friendlyName":"List Categories","resultSummary":"5 found","success":true}""";
        var result = AiChatClient.ParseSseEvent("ToolCallEnd", data);

        result.Should().BeOfType<ChatUpdate.ToolCallEnd>();
        var tc = (ChatUpdate.ToolCallEnd)result!;
        tc.CallId.Should().Be("call_1");
        tc.FriendlyName.Should().Be("List Categories");
        tc.ResultSummary.Should().Be("5 found");
        tc.Success.Should().BeTrue();
    }

    [TestMethod]
    public void ParseSseEvent_ToolCallEnd_CamelCase_FalseSuccess()
    {
        var data = """{"callId":"call_2","friendlyName":"Create Value","resultSummary":"Error: duplicate","success":false}""";
        var result = AiChatClient.ParseSseEvent("ToolCallEnd", data);

        result.Should().BeOfType<ChatUpdate.ToolCallEnd>();
        var tc = (ChatUpdate.ToolCallEnd)result!;
        tc.Success.Should().BeFalse();
    }

    [TestMethod]
    public void ParseSseEvent_Error_CamelCase_DeserializesMessage()
    {
        var data = """{"message":"Something went wrong"}""";
        var result = AiChatClient.ParseSseEvent("Error", data);

        result.Should().BeOfType<ChatUpdate.Error>();
        var err = (ChatUpdate.Error)result!;
        err.Message.Should().Be("Something went wrong");
    }

    [TestMethod]
    public void ParseSseEvent_Error_NullMessage_YieldsDefaultMessage()
    {
        var data = """{"message":null}""";
        var result = AiChatClient.ParseSseEvent("Error", data);

        result.Should().BeOfType<ChatUpdate.Error>();
        var err = (ChatUpdate.Error)result!;
        err.Message.Should().Be("Unknown error");
    }

    [TestMethod]
    public void ParseSseEvent_UnknownEventType_ReturnsNull()
    {
        var data = """{"text":"irrelevant"}""";
        var result = AiChatClient.ParseSseEvent("UnknownEvent", data);
        result.Should().BeNull();
    }

    [TestMethod]
    public void ParseSseEvent_NullEventType_ReturnsNull()
    {
        var data = """{"text":"irrelevant"}""";
        var result = AiChatClient.ParseSseEvent(null, data);
        result.Should().BeNull();
    }

    [TestMethod]
    public void ParseSseEvent_InvalidJson_ReturnsNull()
    {
        var result = AiChatClient.ParseSseEvent("TextChunk", "not valid json");
        result.Should().BeNull();
    }

    [TestMethod]
    public void ParseSseEvent_EmptyJsonObject_TextChunk_YieldsEmptyString()
    {
        var data = "{}";
        var result = AiChatClient.ParseSseEvent("TextChunk", data);

        result.Should().BeOfType<ChatUpdate.TextChunk>();
        var chunk = (ChatUpdate.TextChunk)result!;
        chunk.Text.Should().BeEmpty();
    }

    [TestMethod]
    public void ParseSseEvent_TextChunk_MultiWordText_DeserializesCorrectly()
    {
        var data = """{"text":"Here are the hospital coded values:"}""";
        var result = AiChatClient.ParseSseEvent("TextChunk", data);

        result.Should().BeOfType<ChatUpdate.TextChunk>();
        var chunk = (ChatUpdate.TextChunk)result!;
        chunk.Text.Should().Be("Here are the hospital coded values:");
    }

    [TestMethod]
    public void ParseSseEvent_TextChunk_EscapedJsonInText_DeserializesCorrectly()
    {
        var data = """{"text":"Result: {\\\"id\\\":\\\"123\\\"}"}""";
        var result = AiChatClient.ParseSseEvent("TextChunk", data);

        result.Should().BeOfType<ChatUpdate.TextChunk>();
        var chunk = (ChatUpdate.TextChunk)result!;
        chunk.Text.Should().Contain("Result:");
    }

    [TestMethod]
    public void ParseSseEvent_ToolCallEnd_NullResultSummary_DeserializesWithDefault()
    {
        var data = """{"callId":"call_1","friendlyName":"Test","resultSummary":null,"success":true}""";
        var result = AiChatClient.ParseSseEvent("ToolCallEnd", data);

        result.Should().BeOfType<ChatUpdate.ToolCallEnd>();
        var tc = (ChatUpdate.ToolCallEnd)result!;
        tc.ResultSummary.Should().BeNull();
        tc.Success.Should().BeTrue();
    }

    // This test specifically validates the bug fix: before the fix, camelCase
    // JSON from the server would deserialize with all properties as null/default
    // because System.Text.Json is case-sensitive by default.
    [TestMethod]
    public void ParseSseEvent_CamelCaseBug_TextChunkWouldBeEmptyWithoutFix()
    {
        // This is exactly what the server sends: camelCase property names
        var serverSentData = """{"text":"Hello from AI!"}""";

        var result = AiChatClient.ParseSseEvent("TextChunk", serverSentData);

        result.Should().NotBeNull();
        result.Should().BeOfType<ChatUpdate.TextChunk>();
        var chunk = (ChatUpdate.TextChunk)result!;
        chunk.Text.Should().Be("Hello from AI!",
            "camelCase JSON from server must map to PascalCase DTO properties via PropertyNameCaseInsensitive");
    }

    [TestMethod]
    public void ParseSseEvent_CamelCaseBug_ErrorWouldBeUnknownWithoutFix()
    {
        var serverSentData = """{"message":"API rate limit exceeded"}""";

        var result = AiChatClient.ParseSseEvent("Error", serverSentData);

        result.Should().NotBeNull();
        result.Should().BeOfType<ChatUpdate.Error>();
        var err = (ChatUpdate.Error)result!;
        err.Message.Should().Be("API rate limit exceeded",
            "camelCase JSON from server must map to PascalCase DTO properties");
    }

    // Verifies the IOException-during-cancellation fix:
    // When the CancellationToken fires mid-stream, StreamReader.ReadLineAsync
    // throws IOException ("The I/O operation has been aborted...") rather than
    // OperationCanceledException. The ChatAsync method must swallow this gracefully
    // so the UI doesn't show an error.
    [TestMethod]
    public async Task ChatAsync_IOExceptionDuringCancellation_CompletesGracefully()
    {
        // Arrange: set up a mock SSE server that returns a stream which throws
        // IOException on the second read, simulating cancellation mid-stream
        var handler = new AbortOnReadHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var logger = new TestLogger<AiChatClient>();
        var client = new AiChatClient(http, logger);

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "test")
        };

        // Act: enumerate the stream — the mock handler returns 200 with a stream
        // that yields one SSE event then throws IOException.
        // The IOException catch in the stream-reading loop should handle this gracefully.
        var updates = new List<ChatUpdate>();
        await foreach (var update in client.ChatAsync(history, null, CancellationToken.None))
        {
            updates.Add(update);
        }

        // Assert: we should get the first TextChunk (from before the IOException)
        // and no error update — the IOException is swallowed gracefully.
        updates.Should().ContainSingle(u => u is ChatUpdate.TextChunk,
            "one TextChunk was written before the IOException");
        updates.Should().NotContain(u => u is ChatUpdate.Error,
            "IOException should not surface as an error update");
    }

    /// <summary>
    /// Mock handler that returns a stream which throws IOException on read,
    /// simulating what happens when CancellationToken fires mid-stream.
    /// </summary>
    private class AbortOnReadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var stream = new AbortOnReadStream();
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = content
            });
        }
    }

    /// <summary>
    /// A stream that yields one valid SSE event, then throws IOException on the next read,
    /// simulating the behaviour of .NET's StreamReader when CancellationToken fires mid-stream.
    /// </summary>
    private class AbortOnReadStream : Stream
    {
        private bool _firstEventWritten;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_firstEventWritten)
            {
                _firstEventWritten = true;
                var sse = Encoding.UTF8.GetBytes("event: TextChunk\ndata: {\"text\":\"partial\"}\n\n");
                var len = Math.Min(sse.Length, count);
                Array.Copy(sse, 0, buffer, offset, len);
                return len;
            }

            throw new IOException("The I/O operation has been aborted because of either a thread exit or an application request.");
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_firstEventWritten)
            {
                _firstEventWritten = true;
                var sse = Encoding.UTF8.GetBytes("event: TextChunk\ndata: {\"text\":\"partial\"}\n\n");
                var len = Math.Min(sse.Length, count);
                Array.Copy(sse, 0, buffer, offset, len);
                return Task.FromResult(len);
            }

            throw new IOException("The I/O operation has been aborted because of either a thread exit or an application request.");
        }
    }

    private class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}