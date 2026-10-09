using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// QR-5 (spec §5.2/§5.6): the shared inline instruction editor — one shape, two owners. The media
/// pipeline is driven through its testable seam (the <c>ResourcesSection.StageFileAsync</c>
/// precedent), because FluentInputFile's JS pipeline is not up in bUnit.
/// </summary>
[TestClass]
public class InstructionEditorListBunitTests : BunitContext
{
    public InstructionEditorListBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private IRenderedComponent<InstructionEditorList> Render(
        List<InstructionEditorRow> rows, StageInstructionMediaAsync? stage = null) =>
        Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.StageMediaAsync, stage));

    private static StageInstructionMediaAsync Returning(StagedAttachmentDto? staged, Action? onCall = null) =>
        (row, content, name, type, size) =>
        {
            onCall?.Invoke();
            return Task.FromResult(staged);
        };

    [TestMethod]
    public void ReadOnly_RendersTheBlocksWithoutAffordances()
    {
        var rows = new List<InstructionEditorRow>
        {
            new() { Kind = InstructionKindDto.Text, Text = "Read it twice." },
        };
        var cut = Render<InstructionEditorList>(p => p
            .Add(c => c.Id, "instr")
            .Add(c => c.Rows, rows)
            .Add(c => c.ReadOnly, true));

        cut.FindAll("#instr-add").Should().BeEmpty("View mode offers no add affordance");
        cut.FindAll("#instr-remove-0").Should().BeEmpty("…and no destructive one either");
        cut.Markup.Should().Contain("Read it twice.", "the block's payload is shown, not editable");
    }

    [TestMethod]
    public void Empty_RendersTheEmptyStateAndTheAddAffordance()
    {
        var cut = Render([]);

        cut.FindAll("#instr-empty").Should().ContainSingle();
        cut.Find("#instr-add").Should().NotBeNull();
    }

    [TestMethod]
    public void Add_AppendsATextRow_AndDropsTheEmptyState()
    {
        var rows = new List<InstructionEditorRow>();
        var cut = Render(rows);

        cut.Find("#instr-add").Click();

        rows.Should().ContainSingle().Which.Kind.Should().Be(InstructionKindDto.Text);
        cut.FindAll("#instr-empty").Should().BeEmpty();
    }

    [TestMethod]
    public void Remove_DropsTheRow()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Text } };
        var cut = Render(rows);

        cut.Find("#instr-remove-0").Click();

        rows.Should().BeEmpty();
    }

    [TestMethod]
    public void KindSwitch_RendersThePayloadThatKindNeeds()
    {
        var cut = Render([new InstructionEditorRow { Kind = InstructionKindDto.Url }]);

        cut.Markup.Should().Contain("https://", "a link row takes a URL, not free text");
    }

    [TestMethod]
    public void StagedMedia_RendersTheFileNameAndItsClearAction()
    {
        var cut = Render(
        [
            new InstructionEditorRow
            {
                Kind = InstructionKindDto.Audio,
                FileName = "how-to.mp3",
                ContentType = "audio/mpeg",
                FileSize = 2048,
                StoragePath = "tenants/t/staging/g/how-to.mp3",
            },
        ]);

        cut.Markup.Should().Contain("how-to.mp3");
        cut.Find("#instr-clear-media-0").Should().NotBeNull();
    }

    [TestMethod]
    public async Task StageMediaFile_Success_FillsTheRowFromTheHostsResult()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Audio } };
        var cut = Render(rows, Returning(
            new StagedAttachmentDto("how-to.mp3", "audio/mpeg", 3, "tenants/t/staging/g/how-to.mp3")));

        await cut.InvokeAsync(() => cut.Instance.StageMediaFileAsync(
            rows[0], new MemoryStream([1, 2, 3]), "how-to.mp3", "audio/mpeg", 3));

        rows[0].StoragePath.Should().Be("tenants/t/staging/g/how-to.mp3");
        rows[0].FileName.Should().Be("how-to.mp3");
        rows[0].ContentType.Should().Be("audio/mpeg");
        cut.Markup.Should().NotContain("could not be attached", "a successful stage reports no failure");
    }

    [TestMethod]
    public async Task StageMediaFile_ClientPolicyRejection_NeverCallsTheHost()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Image } };
        var called = false;
        var cut = Render(rows, Returning(staged: null, onCall: () => called = true));

        await cut.InvokeAsync(() => cut.Instance.StageMediaFileAsync(
            rows[0], new MemoryStream([1]), "payload.exe", "application/octet-stream", 1));

        called.Should().BeFalse("the client pre-check refuses it before any round trip");
        cut.Markup.Should().Contain("not allowed");
        rows[0].StoragePath.Should().BeNull();
    }

    [TestMethod]
    public async Task StageMediaFile_AudioRidesTheWidenedAllowList()
    {
        // Round Q1 (owner, 2026-10-09): the media extensions joined the shared allow-list, so an
        // audio instruction file passes the same pre-check a document does — before that, audio and
        // video could not be attached at all.
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Audio } };
        var called = false;
        var cut = Render(rows, Returning(
            new StagedAttachmentDto("how-to.mp3", "audio/mpeg", 3, "tenants/t/staging/g/how-to.mp3"),
            onCall: () => called = true));

        await cut.InvokeAsync(() => cut.Instance.StageMediaFileAsync(
            rows[0], new MemoryStream([1, 2, 3]), "how-to.mp3", "audio/mpeg", 3));

        called.Should().BeTrue("an audio file must reach the host rather than be refused client-side");
        rows[0].StoragePath.Should().Be("tenants/t/staging/g/how-to.mp3");
    }

    [TestMethod]
    public async Task StageMediaFile_HostRefusal_AsksTheAuthorToRetry()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Image } };
        var cut = Render(rows, Returning(staged: null));

        await cut.InvokeAsync(() => cut.Instance.StageMediaFileAsync(
            rows[0], new MemoryStream([1]), "diagram.png", "image/png", 1));

        cut.Markup.Should().Contain("could not be attached");
    }

    [TestMethod]
    public async Task StageMediaFile_WithoutAHostSeam_SaysSo()
    {
        var rows = new List<InstructionEditorRow> { new() { Kind = InstructionKindDto.Image } };
        var cut = Render(rows);

        await cut.InvokeAsync(() => cut.Instance.StageMediaFileAsync(
            rows[0], new MemoryStream([1]), "diagram.png", "image/png", 1));

        cut.Markup.Should().Contain("not available here");
    }
}
