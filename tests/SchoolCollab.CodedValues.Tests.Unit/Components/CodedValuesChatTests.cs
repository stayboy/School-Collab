using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.CodedValues.Admin.Components.Pages.CodedValues;
using SchoolCollab.CodedValues.Admin.Services;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.CodedValues.Tests.Unit.Components;

/// <summary>
/// bUnit tests for the chat component's mode-rendering contract. Verifies
/// that <see cref="CodedValuesChat"/> shows the right pieces in each
/// render mode and honours the <see cref="CodedValuesChat.HideHeader"/>
/// flag.
/// </summary>
[TestClass]
public class CodedValuesChatTests : BunitContext
{
    [TestInitialize]
    public void Setup()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        // The chat's ChatHub injection is scoped; tests that don't exercise
        // the hub can ignore it. Newing it up here keeps the DI graph happy.
        Services.AddSingleton<CodedValuesChatHub>();
        Services.AddSingleton(_ =>
        {
            var http = new HttpClient { BaseAddress = new Uri("http://localhost") };
            return new AiChatClient(http, NullLogger<AiChatClient>.Instance);
        });
    }

    [TestMethod]
    public void CodedValuesChat_FullMode_ShowsHeaderAndClearControl()
    {
        var cut = Render<CodedValuesChat>(parameters => parameters
            .Add(p => p.Mode, CodedValuesChat.CodedValuesChatMode.Full));

        cut.Find(".chat-heading").TextContent.Should().Be("✨ AI Assistant");
        // The Clear control now lives in the model-info row above the input
        // area, rendered as a Hypertext FluentAnchor (not a button).
        cut.Find(".model-info").Should().NotBeNull();
        cut.FindAll("fluent-anchor").Should().Contain(a => a.TextContent.Trim() == "Clear");
    }

    [TestMethod]
    public void CodedValuesChat_DisplayOnlyMode_ShowsClearControlButNoHeader()
    {
        var cut = Render<CodedValuesChat>(parameters => parameters
            .Add(p => p.Mode, CodedValuesChat.CodedValuesChatMode.DisplayOnly));

        // DisplayOnly is the read-only view used by the drawer panel: it shows
        // the mirrored conversation and a Clear control (now in the model-info
        // row) but NO input area. A dead, unwired textbox here was the source
        // of "I type in the drawer and nothing happens" reports — users must
        // prompt from the inline InputOnly chat on the page, which mirrors
        // into this surface.
        cut.FindAll("fluent-anchor").Should().Contain(a => a.TextContent.Trim() == "Clear");
        cut.FindAll(".input-area").Should().BeEmpty();
        cut.FindAll("fluent-button").Should().NotContain(b => b.TextContent.Trim() == "Send");
    }

    [TestMethod]
    public void CodedValuesChat_DisplayOnlyMode_ShowsHeaderByDefault()
    {
        var cut = Render<CodedValuesChat>(parameters => parameters
            .Add(p => p.Mode, CodedValuesChat.CodedValuesChatMode.DisplayOnly));

        // Header is on by default. The panel passes HideHeader=true to suppress
        // it because the surrounding SideDrawer already has the title.
        cut.Find(".chat-heading").TextContent.Should().Be("✨ AI Assistant");
    }

    [TestMethod]
    public void CodedValuesChat_HideHeader_SuppressesHeaderInFullMode()
    {
        var cut = Render<CodedValuesChat>(parameters => parameters
            .Add(p => p.Mode, CodedValuesChat.CodedValuesChatMode.Full)
            .Add(p => p.HideHeader, true));

        cut.FindAll(".chat-heading").Should().BeEmpty();
        cut.FindAll(".chat-intro").Should().BeEmpty();
        // Clear control should still render — HideHeader only affects the header.
        cut.FindAll("fluent-anchor").Should().Contain(a => a.TextContent.Trim() == "Clear");
    }

    [TestMethod]
    public void CodedValuesChat_HideHeader_SuppressesHeaderInDisplayOnlyMode()
    {
        var cut = Render<CodedValuesChat>(parameters => parameters
            .Add(p => p.Mode, CodedValuesChat.CodedValuesChatMode.DisplayOnly)
            .Add(p => p.HideHeader, true));

        cut.FindAll(".chat-heading").Should().BeEmpty();
    }

    [TestMethod]
    public void CodedValuesChat_InputOnlyMode_HidesHeaderAndClearControl()
    {
        var cut = Render<CodedValuesChat>(parameters => parameters
            .Add(p => p.Mode, CodedValuesChat.CodedValuesChatMode.InputOnly));

        cut.FindAll(".chat-heading").Should().BeEmpty();
        // InputOnly is the compact inline bar — no Clear control.
        cut.FindAll("fluent-anchor").Should().BeEmpty();
        // Input area is always rendered (except in InputOnly? no — even InputOnly has the input).
        cut.Find(".input-area").Should().NotBeNull();
    }
}