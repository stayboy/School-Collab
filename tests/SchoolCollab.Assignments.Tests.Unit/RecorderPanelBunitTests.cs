using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Recording materials (spec recording-materials D8/AC-5): the inline recorder's capability gate.
/// Under bUnit's loose JS mode the probe resolves to "unsupported", which is exactly the branch a
/// browser without getUserMedia (insecure context / no API) lands on — so these tests pin the
/// fail-closed rendering: the reason + the pointer at Upload From Device, and NO record controls
/// that could only fail. The supported side is a real-browser concern (the UI tester's scope).
/// </summary>
[TestClass]
public class RecorderPanelBunitTests : BunitContext
{
    public RecorderPanelBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    [TestMethod]
    public void UnsupportedCapability_ShowsTheReasonAndTheUploadPointer()
    {
        var cut = Render<RecorderPanel>(p => p
            .Add(c => c.Id, "rec")
            .Add(c => c.IsVideo, true));

        // The probe has run (loose mode → false), so the fail-closed branch renders…
        cut.WaitForAssertion(() =>
            cut.Find("#rec-unsupported").Should().NotBeNull(
                "an unsupported capability must name the reason instead of rendering dead controls"));

        cut.Markup.Should().Contain("Upload From Device",
            "…pointing the author at the working alternative (never a dead end)");
        cut.Markup.Should().Contain("microphone/camera",
            "the video flavour of the recorder names the camera it needs");
    }

    [TestMethod]
    public void UnsupportedCapability_RendersNoRecordControls()
    {
        // The negative side of the same conditional (testing.md §"Assert the negative"): no Start,
        // no preview element — nothing that could only fail.
        var cut = Render<RecorderPanel>(p => p
            .Add(c => c.Id, "rec")
            .Add(c => c.IsVideo, false));

        cut.WaitForAssertion(() => cut.FindAll("#rec-unsupported").Should().ContainSingle());

        cut.FindAll("#rec-start").Should().BeEmpty("no record control may render without capability");
        cut.FindAll("video").Should().BeEmpty("no camera preview renders when capture is unavailable");
        cut.Markup.Should().Contain("microphone", "the audio flavour names a microphone");
    }

    [TestMethod]
    public void AudioRecorder_DoesNotAskForACamera()
    {
        var cut = Render<RecorderPanel>(p => p
            .Add(c => c.Id, "rec-audio")
            .Add(c => c.IsVideo, false));

        cut.WaitForAssertion(() => cut.Find("#rec-audio-unsupported").Should().NotBeNull());
        // The unsupported copy differentiates the two kinds — audio never mentions /camera.
        cut.Markup.Should().NotContain("/camera",
            "an audio recording never asks for a camera");
    }
}
