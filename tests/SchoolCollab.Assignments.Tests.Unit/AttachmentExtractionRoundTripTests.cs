using FluentAssertions;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// R3 (acceptance criterion 3, the client half; plan-review P1-3) — the attachment extraction outcome
/// must survive the full-replacement save, which means it must ride every hop of the payload.
/// Against <c>0c8912da</c> neither <see cref="NewAttachmentDto"/> nor the editor row had an extraction
/// field at all, so this whole file is new surface.
/// <para>The server's own round-trip is asserted against real Postgres in
/// <c>SchoolCollab.Assignments.Tests.Integration</c>; what is asserted here is that the client never
/// drops the value on the way out or on the way back in.</para>
/// </summary>
[TestClass]
public class AttachmentExtractionRoundTripTests
{
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly DateTimeOffset ExtractedAt = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static AssignmentAuthoringChildrenDto ChildrenWith(params AssignmentAttachmentReadDto[] attachments) =>
        new(AssignmentId, [], attachments, [], []);

    private static AssignmentAttachmentReadDto LoadedAttachment(
        AttachmentExtractionStatusDto status,
        string? text = null,
        string? error = null) =>
        new(Guid.NewGuid(), "syllabus.pdf", "application/pdf", 2048, "tenants/t/staging/syllabus.pdf",
            status, text, ExtractedAt, error);

    // ── Load half: the read value lands on the editor row ────────────────

    [TestMethod]
    public void LoadChildren_CarriesThePersistedExtractionOutcomeOntoTheRow()
    {
        var model = new AssignmentEditFormModel();

        model.LoadChildren(ChildrenWith(
            LoadedAttachment(AttachmentExtractionStatusDto.Succeeded, "photosynthesis body")));

        var row = model.Attachments.Single();
        row.ExtractionStatus.Should().Be(AttachmentExtractionStatusDto.Succeeded);
        row.ExtractedText.Should().Be("photosynthesis body");
        row.ExtractedAt.Should().Be(ExtractedAt);
        row.ExtractionError.Should().BeNull();
        row.Id.Should().NotBeNull("the persisted id is what the re-read action addresses");
    }

    [TestMethod]
    public void LoadChildren_KeepsAFailedStatusAndItsReason()
    {
        var model = new AssignmentEditFormModel();

        model.LoadChildren(ChildrenWith(
            LoadedAttachment(AttachmentExtractionStatusDto.Failed, error: "The file could not be read.")));

        var row = model.Attachments.Single();
        row.ExtractionStatus.Should().Be(AttachmentExtractionStatusDto.Failed);
        row.ExtractedText.Should().BeNull();
        row.ExtractionError.Should().Be("The file could not be read.");
    }

    // ── Projection half: the row reaches the wire ────────────────────────

    [TestMethod]
    public void ToUpdateRequest_EmitsTheExtractionOutcomeOnTheAttachmentPayload()
    {
        // An untouched Edit save must re-send exactly what it loaded — that is the whole of P1-3.
        var model = new AssignmentEditFormModel();
        model.LoadChildren(ChildrenWith(
            LoadedAttachment(AttachmentExtractionStatusDto.Succeeded, "photosynthesis body")));

        var request = model.ToUpdateRequest(
            AssignmentTypeDto.Digital, GradingFormatDto.AutoGraded,
            TargetAudienceTypeDto.AllStudents, Guid.NewGuid(), mandatoryReview: true);

        var dto = request.Attachments!.Single();
        dto.ExtractionStatus.Should().Be(AttachmentExtractionStatusDto.Succeeded);
        dto.ExtractedText.Should().Be("photosynthesis body");
        dto.ExtractedAt.Should().Be(ExtractedAt);
        dto.ExtractionError.Should().BeNull();
    }

    [TestMethod]
    public void ToCreateRequest_EmitsTheExtractionOutcomeOnTheAttachmentPayload()
    {
        var model = new AssignmentEditFormModel();
        model.AddAttachment(new AttachmentEditorRow
        {
            FileName = "syllabus.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            StoragePath = "tenants/t/staging/syllabus.pdf",
            ExtractionStatus = AttachmentExtractionStatusDto.Unsupported,
            ExtractionError = "'.pdf' with no text layer.",
        });

        var request = model.ToCreateRequest(
            AssignmentTypeDto.Digital, GradingFormatDto.AutoGraded,
            TargetAudienceTypeDto.AllStudents, Guid.NewGuid(), mandatoryReview: true);

        var dto = request.Attachments!.Single();
        dto.ExtractionStatus.Should().Be(AttachmentExtractionStatusDto.Unsupported);
        dto.ExtractedText.Should().BeNull();
        dto.ExtractionError.Should().Be("'.pdf' with no text layer.");
    }

    // ── The domain re-normalises whatever the client sends ──────────────

    [TestMethod]
    public void AddAttachment_WithASucceededStatus_KeepsTheTextAndDropsAnyError()
    {
        var assignment = NewAssignment();

        var attachment = assignment.AddAttachment(
            "syllabus.pdf", "application/pdf", 2048, "path",
            AttachmentExtractionStatus.Succeeded, "body", ExtractedAt, "stale error");

        attachment.ExtractionStatus.Should().Be(AttachmentExtractionStatus.Succeeded);
        attachment.ExtractedText.Should().Be("body");
        attachment.ExtractionError.Should().BeNull("a succeeded row cannot also carry a failure reason");
    }

    [TestMethod]
    public void AddAttachment_WithANonSucceededStatus_ClearsAnyText()
    {
        var assignment = NewAssignment();

        var attachment = assignment.AddAttachment(
            "syllabus.pdf", "application/pdf", 2048, "path",
            AttachmentExtractionStatus.Failed, "text that must not survive", ExtractedAt, "unreadable");

        attachment.ExtractionStatus.Should().Be(AttachmentExtractionStatus.Failed);
        attachment.ExtractedText.Should().BeNull("the persisted pair must not contradict itself");
        attachment.ExtractionError.Should().Be("unreadable");
    }

    [TestMethod]
    public void AddAttachment_WithNoExtractionArguments_ReproducesThePreR3Shape()
    {
        var assignment = NewAssignment();

        var attachment = assignment.AddAttachment("syllabus.pdf", "application/pdf", 2048, "path");

        attachment.ExtractionStatus.Should().Be(AttachmentExtractionStatus.NotAttempted);
        attachment.ExtractedText.Should().BeNull();
        attachment.ExtractedAt.Should().BeNull();
        attachment.ExtractionError.Should().BeNull();
    }

    // ── Server-side re-validation of the length (P1-3) ───────────────────

    [TestMethod]
    public void ValidateAttachments_ExtractedTextOverTheCap_IsRejected()
    {
        var overlong = new string('x', AttachmentExtractionLimits.MaxCharacters + 1);

        var act = () => AssignmentContentValidator.ValidateAttachments(
            [new NewAttachmentDto(
                "syllabus.pdf", "application/pdf", 10, "path",
                AttachmentExtractionStatusDto.Succeeded, overlong)],
            new AttachmentUploadOptions());

        act.Should().Throw<AssignmentContentValidationException>()
            .WithMessage("*extracted text longer*");
    }

    [TestMethod]
    public void ValidateAttachments_ExtractedTextAtTheCap_IsAccepted()
    {
        var atCap = new string('x', AttachmentExtractionLimits.MaxCharacters);

        var act = () => AssignmentContentValidator.ValidateAttachments(
            [new NewAttachmentDto(
                "syllabus.pdf", "application/pdf", 10, "path",
                AttachmentExtractionStatusDto.Succeeded, atCap)],
            new AttachmentUploadOptions());

        act.Should().NotThrow();
    }

    [TestMethod]
    public void ValidateAttachments_ExtractionErrorOverTheCap_IsRejected()
    {
        var overlong = new string('e', AttachmentExtractionLimits.MaxErrorLength + 1);

        var act = () => AssignmentContentValidator.ValidateAttachments(
            [new NewAttachmentDto(
                "syllabus.pdf", "application/pdf", 10, "path",
                AttachmentExtractionStatusDto.Failed, null, null, overlong)],
            new AttachmentUploadOptions());

        act.Should().Throw<AssignmentContentValidationException>()
            .WithMessage("*extraction error longer*");
    }

    [TestMethod]
    public void ValidateAttachments_UnknownExtractionStatus_IsRejected()
    {
        var act = () => AssignmentContentValidator.ValidateAttachments(
            [new NewAttachmentDto(
                "syllabus.pdf", "application/pdf", 10, "path",
                (AttachmentExtractionStatusDto)99)],
            new AttachmentUploadOptions());

        act.Should().Throw<AssignmentContentValidationException>()
            .WithMessage("*unknown extraction status*");
    }

    private static Core.Domain.Assignment NewAssignment() =>
        Core.Domain.Assignment.Create(
            "Title", null, Core.Domain.AssignmentType.Digital, Core.Domain.GradingFormat.AutoGraded,
            Core.Domain.TargetAudienceType.AllStudents, Guid.NewGuid(), null, null);
}
