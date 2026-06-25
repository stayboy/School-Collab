using FluentAssertions;
using Microsoft.Extensions.AI;
using SchoolCollab.CodedValues.Admin.Components.Pages.CodedValues;
using SchoolCollab.CodedValues.Admin.Services;

namespace SchoolCollab.CodedValues.Tests.Unit.Services;

[TestClass]
public class CodedValuesChatHubTests
{
    [TestMethod]
    public void NewHub_IsEmpty()
    {
        var hub = new CodedValuesChatHub();

        hub.Messages.Should().BeEmpty();
        hub.StreamingState.IsStreaming.Should().BeFalse();
        hub.StreamingState.StreamingText.Should().BeEmpty();
        hub.StreamingState.ActiveToolCalls.Should().BeNull();
    }

    [TestMethod]
    public void AddMessage_AppendsInOrder()
    {
        var hub = new CodedValuesChatHub();
        var a = new CodedValuesChat.ChatMessageItem(ChatRole.User, "hi");
        var b = new CodedValuesChat.ChatMessageItem(ChatRole.Assistant, "hello");

        hub.AddMessage(a);
        hub.AddMessage(b);

        hub.Messages.Should().Equal(a, b);
    }

    [TestMethod]
    public void AddMessage_RaisesChanged()
    {
        var hub = new CodedValuesChatHub();
        var raised = 0;
        hub.Changed += () => raised++;

        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.User, "hi"));

        raised.Should().Be(1);
    }

    [TestMethod]
    public void AddMessage_RaisesOncePerCall()
    {
        var hub = new CodedValuesChatHub();
        var raised = 0;
        hub.Changed += () => raised++;

        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.User, "a"));
        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.User, "b"));
        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.Assistant, "c"));

        raised.Should().Be(3);
    }

    [TestMethod]
    public void Clear_RemovesAllMessages()
    {
        var hub = new CodedValuesChatHub();
        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.User, "hi"));
        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.Assistant, "hello"));

        hub.Clear();

        hub.Messages.Should().BeEmpty();
    }

    [TestMethod]
    public void Clear_RaisesChanged()
    {
        var hub = new CodedValuesChatHub();
        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.User, "hi"));
        var raised = 0;
        hub.Changed += () => raised++;

        hub.Clear();

        raised.Should().Be(1);
    }

    [TestMethod]
    public void Clear_OnEmptyHub_DoesNotRaiseChanged()
    {
        var hub = new CodedValuesChatHub();
        var raised = 0;
        hub.Changed += () => raised++;

        hub.Clear();

        raised.Should().Be(0);
    }

    [TestMethod]
    public void AddMessage_NullMessage_Throws()
    {
        var hub = new CodedValuesChatHub();

        Action act = () => hub.AddMessage(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void UnsubscribedDelegate_IsNotCalled()
    {
        var hub = new CodedValuesChatHub();
        var raised = 0;
        Action handler = () => raised++;
        hub.Changed += handler;
        hub.Changed -= handler;

        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.User, "hi"));

        raised.Should().Be(0);
    }

    [TestMethod]
    public void SetStreamingState_StoresAndRaisesChanged()
    {
        var hub = new CodedValuesChatHub();
        var raised = 0;
        hub.Changed += () => raised++;
        var state = new CodedValuesChat.ChatStreamingState(
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
        var hub = new CodedValuesChatHub();
        var first = new CodedValuesChat.ChatStreamingState(true, "first", null);
        var second = new CodedValuesChat.ChatStreamingState(true, "second", null);

        hub.SetStreamingState(first);
        hub.SetStreamingState(second);

        hub.StreamingState.StreamingText.Should().Be("second");
    }

    [TestMethod]
    public void SetStreamingState_NullState_Throws()
    {
        var hub = new CodedValuesChatHub();

        Action act = () => hub.SetStreamingState(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void Clear_ResetsStreamingState()
    {
        var hub = new CodedValuesChatHub();
        hub.SetStreamingState(new CodedValuesChat.ChatStreamingState(true, "typing", null));

        hub.Clear();

        hub.StreamingState.IsStreaming.Should().BeFalse();
        hub.StreamingState.StreamingText.Should().BeEmpty();
        hub.StreamingState.ActiveToolCalls.Should().BeNull();
    }

    [TestMethod]
    public void Clear_WhenIdleAndEmpty_DoesNotRaiseChanged()
    {
        var hub = new CodedValuesChatHub();
        var raised = 0;
        hub.Changed += () => raised++;

        hub.Clear();

        raised.Should().Be(0);
    }

    [TestMethod]
    public void Clear_WhenStreamingButNoMessages_RaisesChanged()
    {
        var hub = new CodedValuesChatHub();
        hub.SetStreamingState(new CodedValuesChat.ChatStreamingState(true, "typing", null));
        var raised = 0;
        hub.Changed += () => raised++;

        hub.Clear();

        raised.Should().Be(1);
        hub.StreamingState.IsStreaming.Should().BeFalse();
    }
}