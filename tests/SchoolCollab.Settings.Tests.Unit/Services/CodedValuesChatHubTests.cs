using FluentAssertions;
using Microsoft.Extensions.AI;
using SchoolCollab.AI.Chat.Components;
using SchoolCollab.AI.Chat.Services;

namespace SchoolCollab.Settings.Tests.Unit.Services;

[TestClass]
public class AiChatHubTests
{
    [TestMethod]
    public void NewHub_IsEmpty()
    {
        var hub = new AiChatHub();

        hub.Messages.Should().BeEmpty();
        hub.StreamingState.IsStreaming.Should().BeFalse();
        hub.StreamingState.StreamingText.Should().BeEmpty();
        hub.StreamingState.ActiveToolCalls.Should().BeNull();
    }

    [TestMethod]
    public void AddMessage_AppendsInOrder()
    {
        var hub = new AiChatHub();
        var a = new AiChatMessage(ChatRole.User, "hi");
        var b = new AiChatMessage(ChatRole.Assistant, "hello");

        hub.AddMessage(a);
        hub.AddMessage(b);

        hub.Messages.Should().Equal(a, b);
    }

    [TestMethod]
    public void AddMessage_RaisesChanged()
    {
        var hub = new AiChatHub();
        var raised = 0;
        hub.Changed += () => raised++;

        hub.AddMessage(new AiChatMessage(ChatRole.User, "hi"));

        raised.Should().Be(1);
    }

    [TestMethod]
    public void AddMessage_RaisesOncePerCall()
    {
        var hub = new AiChatHub();
        var raised = 0;
        hub.Changed += () => raised++;

        hub.AddMessage(new AiChatMessage(ChatRole.User, "a"));
        hub.AddMessage(new AiChatMessage(ChatRole.User, "b"));
        hub.AddMessage(new AiChatMessage(ChatRole.Assistant, "c"));

        raised.Should().Be(3);
    }

    [TestMethod]
    public void Clear_RemovesAllMessages()
    {
        var hub = new AiChatHub();
        hub.AddMessage(new AiChatMessage(ChatRole.User, "hi"));
        hub.AddMessage(new AiChatMessage(ChatRole.Assistant, "hello"));

        hub.Clear();

        hub.Messages.Should().BeEmpty();
    }

    [TestMethod]
    public void Clear_RaisesChanged()
    {
        var hub = new AiChatHub();
        hub.AddMessage(new AiChatMessage(ChatRole.User, "hi"));
        var raised = 0;
        hub.Changed += () => raised++;

        hub.Clear();

        raised.Should().Be(1);
    }

    [TestMethod]
    public void Clear_OnEmptyHub_DoesNotRaiseChanged()
    {
        var hub = new AiChatHub();
        var raised = 0;
        hub.Changed += () => raised++;

        hub.Clear();

        raised.Should().Be(0);
    }

    [TestMethod]
    public void AddMessage_NullMessage_Throws()
    {
        var hub = new AiChatHub();

        Action act = () => hub.AddMessage(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void UnsubscribedDelegate_IsNotCalled()
    {
        var hub = new AiChatHub();
        var raised = 0;
        Action handler = () => raised++;
        hub.Changed += handler;
        hub.Changed -= handler;

        hub.AddMessage(new AiChatMessage(ChatRole.User, "hi"));

        raised.Should().Be(0);
    }

    [TestMethod]
    public void SetStreamingState_StoresAndRaisesChanged()
    {
        var hub = new AiChatHub();
        var raised = 0;
        hub.Changed += () => raised++;
        var state = new AiChatStreamingState(
            IsStreaming: true,
            StreamingText: "partial",
            ActiveToolCalls: null);

        hub.SetStreamingState(state);

        hub.StreamingState.Should().Be(state);
        raised.Should().Be(1);
    }

    [TestMethod]
    public void SetStreamingState_OverwritesPreviousState()
    {
        var hub = new AiChatHub();
        var first = new AiChatStreamingState(true, "first", null);
        var second = new AiChatStreamingState(true, "second", null);

        hub.SetStreamingState(first);
        hub.SetStreamingState(second);

        hub.StreamingState.StreamingText.Should().Be("second");
    }

    [TestMethod]
    public void SetStreamingState_NullState_Throws()
    {
        var hub = new AiChatHub();

        Action act = () => hub.SetStreamingState(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void Clear_ResetsStreamingState()
    {
        var hub = new AiChatHub();
        hub.SetStreamingState(new AiChatStreamingState(true, "typing", null));

        hub.Clear();

        hub.StreamingState.IsStreaming.Should().BeFalse();
        hub.StreamingState.StreamingText.Should().BeEmpty();
        hub.StreamingState.ActiveToolCalls.Should().BeNull();
    }

    [TestMethod]
    public void Clear_WhenIdleAndEmpty_DoesNotRaiseChanged()
    {
        var hub = new AiChatHub();
        var raised = 0;
        hub.Changed += () => raised++;

        hub.Clear();

        raised.Should().Be(0);
    }

    [TestMethod]
    public void Clear_WhenStreamingButNoMessages_RaisesChanged()
    {
        var hub = new AiChatHub();
        hub.SetStreamingState(new AiChatStreamingState(true, "typing", null));
        var raised = 0;
        hub.Changed += () => raised++;

        hub.Clear();

        raised.Should().Be(1);
        hub.StreamingState.IsStreaming.Should().BeFalse();
    }
}