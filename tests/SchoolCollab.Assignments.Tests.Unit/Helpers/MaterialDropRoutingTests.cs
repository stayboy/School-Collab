using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Application.Helpers;

namespace SchoolCollab.Assignments.Tests.Unit.Helpers;

/// <summary>
/// D5 (revised, redesign round) — AC-10: a dropped file is ROUTED, not refused wholesale. Media
/// keeps becoming an instruction row; a document now has a destination (a resource) instead of being
/// turned away; only a file the accept-list cannot take is refused, by name.
/// </summary>
[TestClass]
public sealed class MaterialDropRoutingTests
{
    [DataTestMethod]
    [DataRow("clip.mp3", "audio/mpeg")]
    [DataRow("clip.m4a", null)]
    [DataRow("shot.png", "image/png")]
    [DataRow("lesson.mov", "application/octet-stream")]
    public void MediaKinds_BecomeInstructionRows(string fileName, string? contentType)
    {
        MaterialDropRouting.ForDrop(fileName, contentType, 1024)
            .Should().Be(MaterialDropDestination.InstructionMedia,
                "media kinds stay instruction rows — the redesign moved the dropzone, not their destination");
    }

    [DataTestMethod]
    [DataRow("worksheet.pdf", "application/pdf")]
    [DataRow("plan.docx", null)]
    [DataRow("data.csv", "text/csv")]
    public void Documents_BecomeResources_InsteadOfBeingRefused(string fileName, string? contentType)
    {
        MaterialDropRouting.ForDrop(fileName, contentType, 1024)
            .Should().Be(MaterialDropDestination.Resource,
                "D5 revised: a document has a destination now — the blanket refusal was the old behaviour");
    }

    [TestMethod]
    public void UnsupportableFiles_AreRefused_ByName()
    {
        MaterialDropRouting.ForDrop("installer.exe", "application/octet-stream", 1024)
            .Should().Be(MaterialDropDestination.Refused,
                "a file the accept-list cannot take has no destination — refuse rather than drop");

        MaterialDropRouting.ForDrop("huge.pdf", "application/pdf", 1024L * 1024 * 1024)
            .Should().Be(MaterialDropDestination.Refused,
                "over the size cap is refused too — the policy's other gate");

        MaterialDropRouting.NotAcceptedRefusal("installer.exe")
            .Should().Contain("installer.exe", "the reason names the file the author dropped")
            .And.Contain("PDF", "…and says what IS accepted, which is the actionable part");
    }
}
