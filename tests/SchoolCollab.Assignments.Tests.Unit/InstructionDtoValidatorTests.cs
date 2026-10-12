using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// QR-5 (spec <c>question-response-types</c> §5.2/§5.6): the kind→payload pairing for instruction
/// blocks — one rule, two owners (a question or the assignment itself). Pure validation, throws
/// <see cref="AssignmentContentValidationException"/> only, before any child is added.
/// </summary>
[TestClass]
public class InstructionDtoValidatorTests
{
    private static NewInstructionDto TextRow(string? text, string? title = "Read this") => new(
        Kind: InstructionKindDto.Text, Text: text, Url: null,
        FileName: null, ContentType: null, FileSize: 0, StoragePath: null, Title: title);

    private static NewInstructionDto UrlRow(string? url) => new(
        Kind: InstructionKindDto.Url, Text: null, Url: url,
        FileName: null, ContentType: null, FileSize: 0, StoragePath: null);

    private static NewInstructionDto MediaRow(
        InstructionKindDto kind,
        string? fileName = "how-to.mp3",
        string? contentType = "audio/mpeg",
        string? storagePath = "tenants/t/staging/g/how-to.mp3") => new(
        Kind: kind, Text: null, Url: null,
        FileName: fileName, ContentType: contentType, FileSize: 2048, StoragePath: storagePath);

    [TestMethod]
    public void NullList_PassesForEitherOwner()
    {
        var act = () => InstructionDtoValidator.ValidateAll(null, "This assignment");
        act.Should().NotThrow("null means the owner carries no blocks");
    }

    [TestMethod]
    public void ValidRows_OfEveryKind_Pass()
    {
        var rows = new[]
        {
            TextRow("Answer in full sentences."),
            UrlRow("https://example.com/how-to"),
            MediaRow(InstructionKindDto.Audio),
            MediaRow(InstructionKindDto.Video),
            MediaRow(InstructionKindDto.Image),
        };

        var act = () => InstructionDtoValidator.ValidateAll(rows, "This assignment");
        act.Should().NotThrow("each kind carries exactly the payload it implies");
    }

    [TestMethod]
    public void TextRowWithoutTitle_Rejected_AndNamesTheOwnerAndPosition()
    {
        // Round instructional-materials (D6/D7): the title is what a text material is NAMED by, so it is
        // the required half. The body became optional — the case below pins that.
        var act = () => InstructionDtoValidator.ValidateAll(
            [TextRow(text: "Some body", title: null)], "Question 2");

        act.Should().Throw<AssignmentContentValidationException>()
            .WithMessage("Question 2: instruction at position 0: a text instruction needs its title.*");
    }

    [TestMethod]
    public void TextRowWithTitleAndNoBody_IsAccepted()
    {
        var act = () => InstructionDtoValidator.ValidateAll([TextRow(text: null)], "Question 2");

        act.Should().NotThrow("D6 made the body optional — the title is the required half");
    }

    [TestMethod]
    [DataRow("example.com/how-to")]
    [DataRow("/relative/how-to")]
    [DataRow("ftp://example.com/how-to")]
    public void UrlRowThatIsNotAbsoluteHttp_Rejected(string url)
    {
        var act = () => InstructionDtoValidator.ValidateAll([UrlRow(url)], "This assignment");

        act.Should().Throw<AssignmentContentValidationException>()
            .WithMessage("*a link instruction needs an absolute http(s) URL.*");
    }

    [TestMethod]
    [DataRow(InstructionKindDto.Audio)]
    [DataRow(InstructionKindDto.Video)]
    [DataRow(InstructionKindDto.Image)]
    public void MediaRowWithoutStagedMetadata_Rejected(InstructionKindDto kind)
    {
        var act = () => InstructionDtoValidator.ValidateAll(
            [MediaRow(kind, fileName: " ", contentType: null, storagePath: null)], "This assignment");

        act.Should().Throw<AssignmentContentValidationException>()
            .WithMessage("*a media instruction needs the staged file's name, content type and storage path.*");
    }

    [TestMethod]
    public void UnsupportedKind_Rejected()
    {
        var row = new NewInstructionDto(
            Kind: (InstructionKindDto)99, Text: null, Url: null,
            FileName: null, ContentType: null, FileSize: 0, StoragePath: null);

        var act = () => InstructionDtoValidator.ValidateAll([row], "This assignment");

        act.Should().Throw<AssignmentContentValidationException>()
            .WithMessage("*unsupported instruction kind '99'.*");
    }
}
