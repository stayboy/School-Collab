using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The friendly-error seam itself (#416): <see cref="FriendlyError.Describe"/> logs the exception with
/// full detail and returns <b>only</b> the caller's sentence, and <see cref="FriendlyErrorBar"/> renders
/// that sentence — or nothing at all when there is none.
///
/// <para>These are the two halves every converted surface relies on. The page-level proof that a failed
/// load shows the sentence and not the exception text lives beside each harness that has one
/// (<c>ActivityGroupPickerDialogTests</c>, <c>TeacherDetailPageBunitTests</c>); this suite pins the
/// contract itself, so a page cannot regress through the seam.</para>
/// </summary>
[TestClass]
public class FriendlyErrorSeamTests : BunitContext
{
    /// <summary>An <see cref="ILogger"/> that keeps what it was handed, so the test can inspect it.</summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopScope();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception, formatter(state, exception)));

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    public FriendlyErrorSeamTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    /// <summary>
    /// The sentence, verbatim — and none of the exception's own text. This is the whole point: the two
    /// strings are different, and only the caller's is displayable.
    /// </summary>
    [TestMethod]
    public void Describe_ReturnsOnlyThePlainSentence()
    {
        const string Sentence = "Teachers could not be loaded. Try again.";
        var logger = new RecordingLogger();

        var shown = FriendlyError.Describe(
            new InvalidOperationException("An error occurred while sending the request. Socket 1234 refused."),
            Sentence,
            logger);

        shown.Should().Be(Sentence);
        shown.Should().NotContain("Socket", "the exception's own text is never displayable");
        shown.Should().NotContain("1234");
    }

    /// <summary>
    /// The detail is not discarded, it is relocated: one Error entry carrying the exception object itself,
    /// which is what gives the operator the type, message, stack and inner chain.
    /// </summary>
    [TestMethod]
    public void Describe_LogsTheExceptionWithItsFullDetail()
    {
        var logger = new RecordingLogger();
        var failure = new TimeoutException(
            "The request timed out.",
            new IOException("The socket was closed."));

        var shown = FriendlyError.Describe(failure, "The teacher could not be loaded. Try again.", logger);

        logger.Entries.Should().ContainSingle("one failure, one entry — the sentence is not a second log line");
        logger.Entries[0].Level.Should().Be(LogLevel.Error);
        logger.Entries[0].Exception.Should().BeSameAs(
            failure, "the exception object travels to the log, so the stack and inner chain survive");
        logger.Entries[0].Message.Should().Contain(shown, "the entry names the operation in the same words");
    }

    /// <summary>
    /// A blank sentence is refused rather than rendered: a blank bar shows <i>nothing</i>, which would hide
    /// the failure entirely — strictly worse than the raw text it replaced.
    /// </summary>
    [TestMethod]
    public void Describe_RejectsABlankSentence()
    {
        var logger = new RecordingLogger();

        var blank = () => FriendlyError.Describe(new InvalidOperationException("boom"), "   ", logger);

        blank.Should().Throw<ArgumentException>("a blank sentence would hide the failure completely");
        logger.Entries.Should().BeEmpty("nothing was shown, so nothing should be logged as surfaced");
    }

    /// <summary>The render half: the sentence becomes a red bar, and a blank message becomes no bar.</summary>
    [TestMethod]
    public void ErrorBar_RendersTheSentence_AndNothingWhenBlank()
    {
        var cut = Render<FriendlyErrorBar>(p => p.Add(x => x.Message, "Teachers could not be loaded. Try again."));

        cut.Markup.Should().Contain("Teachers could not be loaded. Try again.");
        cut.FindComponents<FluentMessageBar>().Should().ContainSingle(
            "a failure is exactly one bar, and it is an error");

        var blank = Render<FriendlyErrorBar>(p => p.Add(x => x.Message, "   "));
        blank.FindAll("fluent-message-bar").Should().BeEmpty(
            "a blank message is not an empty red bar — it is no bar at all, which keeps a failed state "
            + "from reading as an empty one");
    }
}
