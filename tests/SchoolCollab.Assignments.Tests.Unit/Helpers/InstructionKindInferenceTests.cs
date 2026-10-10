using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit.Helpers;

/// <summary>
/// D4/D5 (spec <c>instructional-materials</c>) — AC-3: the dropzone's kind inference and its
/// refusal. A dropped file with a representable kind appends the matching row; a document is
/// refused with a named reason pointing at the file-material path, never accepted and dropped.
/// </summary>
[TestClass]
public sealed class InstructionKindInferenceTests
{
    [TestMethod]
    public void Audio_Video_And_Image_ContentTypes_MapToTheirKinds()
    {
        InstructionKindInference.ForFile("clip.mp3", "audio/mpeg").Should().Be(InstructionKindDto.Audio);
        InstructionKindInference.ForFile("clip.mp3", "video/mp4").Should().Be(InstructionKindDto.Video,
            "the declared type wins when it names a kind — a mislabelled extension must not silently reclassify");
        InstructionKindInference.ForFile("shot.png", "image/png").Should().Be(InstructionKindDto.Image);
    }

    [TestMethod]
    public void Extensions_Decide_When_No_Usable_ContentType_Arrives()
    {
        InstructionKindInference.ForFile("clip.m4a", null).Should().Be(InstructionKindDto.Audio,
            "a browser may send no type at all");
        InstructionKindInference.ForFile("clip.wav", "application/octet-stream").Should().Be(InstructionKindDto.Audio,
            "…or a useless one — the extension is the second signal, not a fallback of last resort");
        InstructionKindInference.ForFile("lesson.MOV", " ").Should().Be(InstructionKindDto.Video,
            "matching is case-insensitive: uploads are not consistently lower-cased");
        InstructionKindInference.ForFile("photo.webp", "application/octet-stream").Should().Be(InstructionKindDto.Image);
    }

    [TestMethod]
    public void Documents_Are_Refused_Not_Guessed()
    {
        InstructionKindInference.ForFile("worksheet.pdf", "application/pdf").Should().BeNull(
            "D5/D13: no instruction kind can represent a document — refusing beats accepting and dropping");
        InstructionKindInference.ForFile("plan.docx", null).Should().BeNull();
        InstructionKindInference.ForFile("data.csv", "text/csv").Should().BeNull();
    }

    [TestMethod]
    public void The_Refusal_Names_The_File_And_The_File_Material_Path()
    {
        var reason = InstructionKindInference.DocumentRefusal("worksheet.pdf");

        reason.Should().Contain("worksheet.pdf", "the author must know WHICH drop was refused")
            .And.Contain("Upload from device", "…and what to use instead (AC-3's named reason)");
    }
}
