using FluentAssertions;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// AC-11 (spec <c>recording-materials</c> D7) — the raised upload caps stay in LOCKSTEP across the
/// client policy and the server options, and the recorder's byte-stop guard sits safely under the
/// per-file ceiling. The caps are fanned out to several places (AttachmentUploadOptions,
/// AttachmentUploadPolicy, the AppHost parameter defaults, documents/configuration.md); a drift between
/// the client pre-check and the server authority would let a take the client accepted be rejected by the
/// server (or vice-versa). This pins the two code-side values so a future edit to one fails here.
/// </summary>
[TestClass]
public class AttachmentUploadCapLockstepTests
{
    [TestMethod]
    public void ClientAndServer_PerFileCaps_Match()
    {
        // The client policy is the friendly UX mirror; the server options are authoritative. They must
        // agree so a file the client accepts is never rejected by the server for size (and vice-versa).
        AttachmentUploadPolicy.MaxFileSizeBytes
            .Should().Be(new AttachmentUploadOptions().MaxFileSizeBytes,
                "the client per-file cap must match the server's so a take passes both checks");
    }

    [TestMethod]
    public void RecorderAutoStop_SitsUnderPerFileCap()
    {
        // D7: the byte-stop is a guard that halts a take BEFORE the server can 413 it — so it must be
        // strictly under the per-file cap, leaving headroom for the multipart overhead the stage request adds.
        AttachmentUploadPolicy.RecorderAutoStopBytes
            .Should().BeLessThan(AttachmentUploadPolicy.MaxFileSizeBytes,
                "the recorder auto-stop must trip before the per-file cap so a take is never lost to a 413");
    }

    [TestMethod]
    public void TotalCap_ExceedsPerFileCap()
    {
        // D7: the total must exceed the per-file cap, or a single recording would consume the whole budget.
        new AttachmentUploadOptions().MaxTotalSizeBytes
            .Should().BeGreaterThan(new AttachmentUploadOptions().MaxFileSizeBytes,
                "the total cap must exceed the per-file cap so more than one file can attach");
    }
}
