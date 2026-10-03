using FluentAssertions;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// R3 (acceptance criterion 6, the budget half; plan-review P1-6) — the AI generation request's
/// <c>ResourceTexts</c> is filled from ONE budget shared by URL texts and attachment texts. Nothing
/// here exists against <c>0c8912da</c>: attachments could not ground a generation at all, and the only
/// composer was an inline <c>List&lt;string&gt;</c> inside the Razor section.
/// <para>The AI host rejects more than five entries and any entry longer than 20 000 characters with a
/// 400, so the merge policy is asserted against exactly those two numbers.</para>
/// </summary>
[TestClass]
public class ResourceTextBudgetTests
{
    [TestMethod]
    public void Compose_UrlTextsComeFirst_ThenAttachmentsFillTheRemainingSlots()
    {
        var texts = ResourceTextBudget.Compose(
            urlTexts: ["url-1", "url-2"],
            attachmentTexts: ["pdf-body", "docx-body"]);

        texts.Should().Equal(
            ["url-1", "url-2", "pdf-body", "docx-body"],
            "URL texts are author-chosen and are placed first; attachments take what is left");
    }

    [TestMethod]
    public void Compose_MoreTextsThanSlots_IsCappedAtTheAiHostsLimit_AndTheOverflowIsDropped()
    {
        var texts = ResourceTextBudget.Compose(
            urlTexts: ["url-1", "url-2", "url-3"],
            attachmentTexts: ["a-1", "a-2", "a-3", "a-4"]);

        texts.Should().HaveCount(ResourceTextBudget.MaxResourceTexts);
        texts.Should().Equal(
            ["url-1", "url-2", "url-3", "a-1", "a-2"],
            "the last two attachments are dropped fail-open — never sent to a host that would 400 them");
    }

    [TestMethod]
    public void Compose_EntryLongerThanTheLimit_IsTruncatedToTheLimit()
    {
        var overlong = new string('z', ResourceTextBudget.MaxResourceTextLength + 5_000);

        var texts = ResourceTextBudget.Compose(urlTexts: [overlong], attachmentTexts: null);

        texts.Should().HaveCount(1);
        texts![0].Length.Should().Be(ResourceTextBudget.MaxResourceTextLength);
    }

    [TestMethod]
    public void Compose_EntryExactlyAtTheLimit_IsNotTruncated()
    {
        var exact = new string('z', ResourceTextBudget.MaxResourceTextLength);

        var texts = ResourceTextBudget.Compose(urlTexts: [exact], attachmentTexts: null);

        texts.Should().Equal(exact);
    }

    [TestMethod]
    public void Compose_WithNoAttachments_IsByteEquivalentToTheUrlOnlyComposition()
    {
        // Criterion 6's second half: a request with no attachment text must be indistinguishable from
        // the pre-R3 one. The pre-R3 section handed the AI host the raw URL texts, or null for none —
        // reproduced here literally so a future "helpful" trim/normalise in the composer cannot slip in.
        IReadOnlyList<string> urlTexts = ["extracted-1", "extracted-2"];
        IReadOnlyList<string>? beforeR3 = urlTexts.Count > 0 ? urlTexts : null;

        var after = ResourceTextBudget.Compose(urlTexts, attachmentTexts: []);

        after.Should().Equal(beforeR3!);
    }

    [TestMethod]
    public void Compose_NothingQualifies_ReturnsNull_NotAnEmptyList()
    {
        // An empty list is a different wire value from null, and the AI host validates `Count > 5` on
        // what it receives — the pre-R3 composer normalised "nothing" to null, and so must this one.
        ResourceTextBudget.Compose(null, null).Should().BeNull();
        ResourceTextBudget.Compose([], []).Should().BeNull();
        ResourceTextBudget.Compose([""], null).Should().BeNull();
    }

    [TestMethod]
    public void Compose_EmptyAttachmentText_IsSkippedWithoutConsumingASlot()
    {
        var texts = ResourceTextBudget.Compose(
            urlTexts: ["url-1"],
            attachmentTexts: ["", "real-body"]);

        texts.Should().Equal(
            ["url-1", "real-body"],
            "a blank extraction must not burn one of the five slots");
    }

    // ── R3-2: the budget reports its own attachment-side overflow (the UI never re-derives it) ──

    [TestMethod]
    public void ComposeWithReport_ThreeUrlsAndThreeAttachments_ReportsTheOneAttachmentTheBudgetDropped()
    {
        // The F1 shape: three reference URLs consume three of the five slots, so only two of the three
        // readable attachments can ground the generation — and every row still reads "extracted". The
        // budget itself must say so: before this, Compose returned a silent five-entry list and no caller
        // could tell an included attachment from a dropped one.
        var result = ResourceTextBudget.ComposeWithReport(
            urlTexts: ["url-1", "url-2", "url-3"],
            attachmentTexts: ["a-1", "a-2", "a-3"]);

        result.Texts.Should().Equal(
            ["url-1", "url-2", "url-3", "a-1", "a-2"],
            "URL texts are author-chosen and are placed first; attachments take what is left");
        result.Texts.Should().HaveCount(ResourceTextBudget.MaxResourceTexts);
        result.DroppedAttachmentCount.Should().Be(
            1,
            "a-3 was read successfully and lost its slot to the full budget — the surface must be able " +
            "to say that out loud instead of letting the row read as if it had grounded the generation");
    }

    [TestMethod]
    public void ComposeWithReport_AttachmentsAllFit_ReportsNoDrop()
    {
        var result = ResourceTextBudget.ComposeWithReport(
            urlTexts: ["url-1", "url-2"],
            attachmentTexts: ["a-1", "a-2", "a-3"]);

        result.Texts.Should().HaveCount(ResourceTextBudget.MaxResourceTexts);
        result.DroppedAttachmentCount.Should().Be(0,
            "a budget that swallowed nothing must not warn the author about anything");
    }

    [TestMethod]
    public void ComposeWithReport_NullOrEmptyAttachmentText_IsNotCountedAsDropped()
    {
        // Only text that WOULD have grounded the generation had a slot been free is a drop. A null or
        // blank extraction never occupied a slot and never wanted one, so counting it would overstate
        // the loss to the author (and, with one blank per unreadable file, could warn about nothing).
        IReadOnlyList<string> attachmentTexts = [null!, "", "a-body", "b-body"];

        var result = ResourceTextBudget.ComposeWithReport(
            urlTexts: ["url-1", "url-2", "url-3", "url-4"],
            attachmentTexts: attachmentTexts);

        result.Texts.Should().Equal(["url-1", "url-2", "url-3", "url-4", "a-body"],
            "the null and blank entries are skipped without consuming the last slot");
        result.DroppedAttachmentCount.Should().Be(
            1,
            "only b-body was a real candidate that lost its slot — the null and blank entries were " +
            "never candidates");
    }

    [TestMethod]
    public void Limits_MirrorTheAiHostsOwnConstants()
    {
        // The composer must never build a request the AI host would reject. Both numbers are mirrored
        // from AssignmentQuestionGenerationService, which this project cannot reference — so they are
        // pinned here against the values that service validates with.
        ResourceTextBudget.MaxResourceTexts.Should().Be(5);
        ResourceTextBudget.MaxResourceTextLength.Should().Be(20000);

        // R3/D8: one attachment's retained text must fit the AI host's per-entry limit whole, or the
        // grounding would silently lose the tail of every attachment.
        AttachmentExtractionLimits.MaxCharacters.Should().Be(ResourceTextBudget.MaxResourceTextLength);
    }

    [TestMethod]
    public void AttachmentExtractionLimits_KeepTheErrorBoundedAndTheInputCapBelowTheStageRefusal()
    {
        AttachmentExtractionLimits.MaxErrorLength.Should().Be(500);
        AttachmentExtractionLimits.MaxInputBytes.Should().BeLessThan(
            new AttachmentUploadOptions().MaxFileSizeBytes,
            "the extraction cap is a softer, separate ceiling than the stage refusal");
        AttachmentExtractionStatus.NotAttempted.Should().Be(
            (AttachmentExtractionStatus)0,
            "the persisted default for a pre-R3 row must be NotAttempted");
    }
}
