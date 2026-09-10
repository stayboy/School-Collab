using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using SchoolCollab.AI.Abstractions;
using SchoolCollab.AI.Services;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Tests for <see cref="AssignmentQuestionGenerationSystemPromptProvider"/>.
/// The class writes into <c>{AppContext.BaseDirectory}/Prompts</c> for the
/// Development file-override tests, so the file is marked
/// <c>[DoNotParallelize]</c> to avoid racing the project-wide
/// <c>Parallelize(Scope.MethodLevel)</c> setting.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AssignmentQuestionGenerationSystemPromptProviderTests
{
    private readonly string _promptsDir = Path.Combine(AppContext.BaseDirectory, "Prompts");
    private readonly string _activeFile = Path.Combine(AppContext.BaseDirectory, "Prompts", "assignment-question-system-prompt.md");
    private readonly string _originalFile = Path.Combine(AppContext.BaseDirectory, "Prompts", "assignment-question-system-prompt.original.md");

    [TestInitialize]
    public void Setup()
    {
        // Defensive — the embedded-resource fallback ensures the prompt loads
        // even if the Development files have been deleted.
        if (!Directory.Exists(_promptsDir))
            Directory.CreateDirectory(_promptsDir);
    }

    [TestMethod]
    public async Task GetSystemPromptAsync_Production_LoadsEmbeddedPrompt_AndIncludesToolListIsFalse()
    {
        // Arrange — Production environment → only embedded resource is read.
        var provider = BuildProvider("Production", out _);

        // Act
        var prompt = await provider.GetSystemPromptAsync(CancellationToken.None);

        // Assert
        prompt.Should().NotBeNullOrWhiteSpace("the embedded resource must always be available");
        prompt.Should().Contain("questions", "the embedded prompt defines the questions JSON contract");
        provider.IncludesToolList.Should().BeFalse("v1 ships zero tools");
    }

    [TestMethod]
    public async Task GetSystemPromptAsync_DevelopmentFileOverride_WinsOverEmbedded()
    {
        // Arrange — Development environment + an override file present.
        var provider = BuildProvider("Development", out _);

        // Save and restore: the override file must not affect other tests.
        var originalContents = File.Exists(_activeFile) ? File.ReadAllText(_activeFile) : null;
        try
        {
            File.WriteAllText(_activeFile, "OVERRIDE-VIA-FILE");
            // Touch the mtime to a known distinct value so the cache invalidates.
            File.SetLastWriteTimeUtc(_activeFile, DateTime.UtcNow);

            // Act
            var prompt = await provider.GetSystemPromptAsync(CancellationToken.None);

            // Assert
            prompt.Should().Be("OVERRIDE-VIA-FILE",
                "Development file override must take precedence over the embedded resource");
        }
        finally
        {
            if (originalContents is not null)
                File.WriteAllText(_activeFile, originalContents);
            else if (File.Exists(_activeFile))
                File.Delete(_activeFile);
        }
    }

    [TestMethod]
    public async Task GetSystemPromptAsync_MtimeCache_ReloadsOnlyWhenFileChanges()
    {
        // Arrange
        var provider = BuildProvider("Development", out _);

        var originalContents = File.Exists(_activeFile) ? File.ReadAllText(_activeFile) : null;
        try
        {
            // Force a stable, distinguishable mtime we control.
            var firstMtime = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var secondMtime = new DateTime(2030, 1, 1, 12, 0, 5, DateTimeKind.Utc);

            File.WriteAllText(_activeFile, "FIRST-LOAD");
            File.SetLastWriteTimeUtc(_activeFile, firstMtime);

            var firstLoad = await provider.GetSystemPromptAsync(CancellationToken.None);
            firstLoad.Should().Be("FIRST-LOAD");

            // Second load with the same mtime → cache hit (returns the same string).
            var secondLoad = await provider.GetSystemPromptAsync(CancellationToken.None);
            secondLoad.Should().Be("FIRST-LOAD", "mtime-cache must return the cached value when the file is unchanged");

            // Modify the file content but keep the mtime the same → still cached.
            File.WriteAllText(_activeFile, "SECOND-LOAD-SAME-MTIME");
            File.SetLastWriteTimeUtc(_activeFile, firstMtime);
            var thirdLoad = await provider.GetSystemPromptAsync(CancellationToken.None);
            thirdLoad.Should().Be("FIRST-LOAD", "the mtime cache must not reload when only content changed");

            // Now bump the mtime → cache invalidates.
            File.WriteAllText(_activeFile, "SECOND-LOAD");
            File.SetLastWriteTimeUtc(_activeFile, secondMtime);
            var fourthLoad = await provider.GetSystemPromptAsync(CancellationToken.None);
            fourthLoad.Should().Be("SECOND-LOAD", "mtime change must invalidate the cache");
        }
        finally
        {
            if (originalContents is not null)
                File.WriteAllText(_activeFile, originalContents);
            else if (File.Exists(_activeFile))
                File.Delete(_activeFile);
        }
    }

    [TestMethod]
    public void BuildMessages_NoPromptOverride_ReturnsSystemPlusUser()
    {
        var provider = BuildProvider("Production", out _);
        var request = new QuestionGenerationRequest(
            TopicId: Guid.NewGuid(),
            TopicName: "Photosynthesis");

        // Act
        var messages = provider.BuildMessages(request);

        // Assert
        messages.Should().HaveCount(2, "no PromptOverride means no third user message");
        messages[0].Role.Should().Be(ChatRole.System);
        messages[0].Text.Should().NotBeNullOrWhiteSpace("the system prompt must load from the embedded resource");
        messages[1].Role.Should().Be(ChatRole.User);
        messages[1].Text.Should().Contain("Photosynthesis", "the user message must serialise the request payload as JSON");
    }

    [TestMethod]
    public void BuildMessages_WithPromptOverride_AddsThirdUserMessage_AndKeepsSystemUnchanged()
    {
        var provider = BuildProvider("Production", out _);
        var request = new QuestionGenerationRequest(
            TopicId: Guid.NewGuid(),
            TopicName: "Photosynthesis",
            PromptOverride: "Make the questions focus on cellular biology.");

        // Act
        var messages = provider.BuildMessages(request);

        // Assert
        messages.Should().HaveCount(3, "PromptOverride must add a third user-role message (EC-9)");
        messages[0].Role.Should().Be(ChatRole.System);
        messages[1].Role.Should().Be(ChatRole.User);
        messages[1].Text.Should().Contain("Photosynthesis");
        messages[2].Role.Should().Be(ChatRole.User, "the override must be user-role framing, never merged into the system prompt");
        messages[2].Text.Should().Contain("Make the questions focus on cellular biology.");
        messages[0].Text.Should().NotContain("Make the questions focus on cellular biology.",
            "EC-9: the system prompt must remain unchanged when an override is supplied");
    }

    private static AssignmentQuestionGenerationSystemPromptProvider BuildProvider(
        string environmentName,
        out Mock<IHostEnvironment> mockEnv)
    {
        mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns(environmentName);
        return new AssignmentQuestionGenerationSystemPromptProvider(
            mockEnv.Object,
            new TestLogger<AssignmentQuestionGenerationSystemPromptProvider>());
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
