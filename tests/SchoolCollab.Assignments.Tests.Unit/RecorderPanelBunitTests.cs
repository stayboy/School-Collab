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

    /// <summary>Round-recorder-takeover AC-2 (Q3): the header — title + labeled Cancel — renders in
    /// EVERY state, the unsupported one included, because the takeover's close affordance must never
    /// be unreachable (even where capture cannot work).</summary>
    [TestMethod]
    public void Header_RendersInTheUnsupportedState()
    {
        var cut = Render<RecorderPanel>(p => p
            .Add(c => c.Id, "rec")
            .Add(c => c.IsVideo, true));

        cut.WaitForAssertion(() => cut.Find("#rec-unsupported").Should().NotBeNull());

        cut.Find("#rec-title").TextContent.Should().Be("Recording video",
            "the header names what is being recorded");
        cut.Find("#rec-cancel").Should().NotBeNull(
            "the labeled Cancel is present even where capture is unavailable");
    }

    /// <summary>AC-3 (Q5): Cancel fires OnClose — the EventCallback seam the host closes the takeover
    /// through. The panel resets its own state before notifying; disposal stays the host's unmount +
    /// DisposeAsync, never Cancel.</summary>
    [TestMethod]
    public void Cancel_FiresOnCloseForTheHost()
    {
        var closed = 0;
        var cut = Render<RecorderPanel>(p => p
            .Add(c => c.Id, "rec")
            .Add(c => c.IsVideo, false)
            .Add(c => c.OnClose, () => closed++));

        cut.WaitForAssertion(() => cut.Find("#rec-unsupported").Should().NotBeNull());

        cut.Find("#rec-cancel").Click();

        closed.Should().Be(1, "the host closes the takeover when the panel reports a cancel");
    }

    /// <summary>Owner pill rework (2026-10-12): a SEEDED panel reopens as the staged take's preview
    /// player — the same player + Record again, never the commit button — and renders AHEAD of the
    /// capability branches: playback needs no getUserMedia capability (which loose-mode lacks).</summary>
    [TestMethod]
    public void SeededReview_RendersPlayerAndRetake_WithoutCommit()
    {
        var cut = Render<RecorderPanel>(p => p
            .Add(c => c.Id, "rec")
            .Add(c => c.IsVideo, false)
            .Add(c => c.Seed, new RecorderPanel.ReviewSeed("blob:staged", "r.webm", 10)));

        cut.Find("#rec-preview-take").Should().NotBeNull(
            "the take's player renders immediately, no probe needed");
        cut.Find("#rec-retake").Should().NotBeNull("Record again lives on the review panel");
        cut.FindAll("#rec-commit").Should().BeEmpty(
            "a reopened take has nothing new to commit — it is already staged");
        cut.Find("#rec-title").TextContent.Should().Be("Recording audio",
            "the header still names the surface");
    }

    /// <summary>Hold verdict 2026-10-12 (P1): a SEEDED (reopened) URL is the host's — the panel's
    /// teardown must never revoke it (unguarded, DisposeAsync killed the pill's Blob URL). The positive
    /// side (an owned URL IS revoked) is browser-scope: the in-session Review state is unreachable
    /// under bUnit (endRecording's private return type) — the round-recorder-takeover honest-gap
    /// precedent.</summary>
    [TestMethod]
    public async Task SeededReview_Dispose_DoesNotRevokeTheSeededUrl()
    {
        var module = JSInterop.SetupModule(
            "./_content/SchoolCollab.Assignments.Application/Components/Pages/Assignments/RecorderPanel.razor.js");
        var cut = Render<RecorderPanel>(p => p
            .Add(c => c.Id, "rec")
            .Add(c => c.IsVideo, false)
            .Add(c => c.Seed, new RecorderPanel.ReviewSeed("blob:staged", "r.webm", 10)));

        cut.Find("#rec-preview-take").Should().NotBeNull("the seeded review renders the take's player");

        await cut.InvokeAsync(async () => await cut.Instance.DisposeAsync());

        module.VerifyNotInvoke("revokePreviewUrl");
    }

    /// <summary>Hold verdict 2026-10-12 (finding 3): a retake from a REOPENED video review must bind
    /// the live preview AFTER the recording branch renders — the reopened branch renders only the
    /// take's player, so startCapture's bind-by-id at call time is a silent no-op.</summary>
    [TestMethod]
    public void SeededReview_Retake_RebindsTheLivePreviewAfterTheRecordingBranchRenders()
    {
        var module = JSInterop.SetupModule(
            "./_content/SchoolCollab.Assignments.Application/Components/Pages/Assignments/RecorderPanel.razor.js");
        var cut = Render<RecorderPanel>(p => p
            .Add(c => c.Id, "rec")
            .Add(c => c.IsVideo, true)
            .Add(c => c.Seed, new RecorderPanel.ReviewSeed("blob:staged", "r.webm", 10)));

        cut.Find("#rec-preview-take").Should().NotBeNull("the reopened review shows the take's player");
        cut.Find("#rec-retake").Click();

        // The defect fix: the panel defers the live-preview bind until AFTER the recording branch has
        // rendered. Under loose mode the capability probe reports unsupported, so the recording branch
        // is not the one that renders the preview element — but the deferred call itself is exactly
        // what the reopened-retake path was missing (startCapture's synchronous bind-by-id no-ops).
        cut.WaitForAssertion(() => module.VerifyInvoke("bindPreview"));
        cut.FindAll("#rec-preview-take").Should().BeEmpty(
            "the recording state replaced the reopened review — the take's player is gone");
    }

    /// <summary>P3 residual closed (2026-10-12): the deferred bind's flag is consumed only when the
    /// module/state guards pass. It used to be cleared unconditionally BEFORE them, so a render landing
    /// while StartAsync still awaits interop (module not yet imported, state still Idle) spent the flag
    /// unbound and left the live preview dark for the whole take.
    /// <para><b>Honest gap (browser-scope, the DisposeAsync precedent above):</b> that window is not
    /// reproducible under bUnit. `_state = Recording` (RecorderPanel.razor:302) and `_module ??= import`
    /// (:270) both complete inside the awaited StartAsync, and WaitForAssertion only re-renders after
    /// that await settles — so the guards never fail here and both orderings report one bind. This test
    /// therefore pins what IS observable: the retake binds the live preview exactly once and never
    /// re-binds on later renders (the flag is spent, not left armed). The guard-failure ordering itself
    /// was verified by reading the code path, and needs a real browser to exercise.</para></summary>
    [TestMethod]
    public void SeededReview_Retake_BindsTheLivePreviewExactlyOnce()
    {
        var module = JSInterop.SetupModule(
            "./_content/SchoolCollab.Assignments.Application/Components/Pages/Assignments/RecorderPanel.razor.js");
        var cut = Render<RecorderPanel>(p => p
            .Add(c => c.Id, "rec")
            .Add(c => c.IsVideo, true)
            .Add(c => c.Seed, new RecorderPanel.ReviewSeed("blob:staged", "r.webm", 10)));

        cut.Find("#rec-preview-take").Should().NotBeNull("the reopened review shows the take's player");
        cut.Find("#rec-retake").Click();

        cut.WaitForAssertion(() => module.VerifyInvoke("bindPreview"),
            TimeSpan.FromSeconds(5));

        JSInterop.Invocations.Count(i => i.Identifier == "bindPreview").Should().Be(1,
            "the flag must be spent by the bind — a second render must not re-bind the element");
    }
}
