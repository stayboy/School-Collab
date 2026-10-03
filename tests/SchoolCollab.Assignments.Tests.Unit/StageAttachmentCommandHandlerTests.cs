using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.StageAttachmentCommand;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-A1 coverage for <see cref="StageAttachmentCommandHandler"/> +
/// <see cref="StagedFileValidator"/> (FR-210). Hand-rolled
/// <see cref="FakeFileStore"/> captures the stream / metadata so the
/// suite asserts the validation ordering: a rejected payload results
/// in ZERO calls to the IFileStore (the per-file cap is the round trip
/// safety; size rejections carry <see cref="StagedFileRejectedException.IsSizeLimit"/>
/// so the endpoint maps them to <c>413</c>).
/// </summary>
[TestClass]
public class StageAttachmentCommandHandlerTests
{
    private sealed class FakeFileStore : IFileStore
    {
        public int StoreCalls { get; private set; }
        public string StoredPath { get; set; } = "tenants/t/staging/g/seed.pdf";
        public string LastFileName { get; private set; } = string.Empty;
        public string LastContentType { get; private set; } = string.Empty;
        public byte[] LastBytes { get; private set; } = [];

        public Task<string> StoreAsync(Stream content, string fileName, string contentType,
            CancellationToken cancellationToken = default)
        {
            StoreCalls++;
            LastFileName = fileName;
            LastContentType = contentType;
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            LastBytes = ms.ToArray();
            return Task.FromResult(StoredPath);
        }

        /// <summary>R3 (D7): the handler re-reads what it just stored, so the fake must serve the
        /// stored bytes back — the extraction half of the stage request runs off this stream.</summary>
        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            OpenReadCalls++;
            LastOpenPath = storagePath;
            if (OpenReadThrows is not null) throw OpenReadThrows;
            return Task.FromResult<Stream>(new MemoryStream(LastBytes));
        }

        public int OpenReadCalls { get; private set; }
        public string? LastOpenPath { get; private set; }

        /// <summary>When set, the re-read throws instead of serving the bytes (a blob swept between
        /// the store and the read).</summary>
        public Exception? OpenReadThrows { get; set; }

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>R3 (D7) — hand-rolled <see cref="IAttachmentTextExtractor"/> so the stage path's
    /// fail-open contract is asserted without depending on PdfPig/OpenXml.</summary>
    private sealed class FakeExtractor : IAttachmentTextExtractor
    {
        public int ExtractCalls { get; private set; }
        public string? LastFileName { get; private set; }
        public long LastFileSize { get; private set; }

        public AttachmentExtractionResult Next { get; set; } =
            AttachmentExtractionResult.Succeeded("extracted body", 14);

        /// <summary>When set, extraction throws — the handler's defence-in-depth path.</summary>
        public Exception? ThrowOnExtract { get; set; }

        public Task<AttachmentExtractionResult> ExtractAsync(
            Stream content, string fileName, long fileSize, CancellationToken cancellationToken = default)
        {
            ExtractCalls++;
            LastFileName = fileName;
            LastFileSize = fileSize;
            if (ThrowOnExtract is not null) throw ThrowOnExtract;
            return Task.FromResult(Next);
        }
    }

    private static StageAttachmentCommandHandler NewHandler(
        IFileStore fileStore,
        IOptions<AttachmentUploadOptions>? uploadOptions = null,
        IAttachmentTextExtractor? extractor = null)
    {
        return new StageAttachmentCommandHandler(
            fileStore,
            extractor ?? new FakeExtractor(),
            uploadOptions ?? Options.Create(new AttachmentUploadOptions()),
            NullLogger<StageAttachmentCommandHandler>.Instance);
    }

    private static StageAttachmentCommand StageCommand(
        string fileName, string contentType, long fileSize, string body = "PDFDATA")
        => new(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body)), fileName, contentType, fileSize);

    [TestMethod]
    public async Task HandleAsync_HappyPath_ReturnsMetadataAndCapturesStream()
    {
        var store = new FakeFileStore();
        var handler = NewHandler(store);

        var dto = await handler.HandleAsync(StageCommand("notes.pdf", "application/pdf", 7));

        dto.FileName.Should().Be("notes.pdf");
        dto.ContentType.Should().Be("application/pdf");
        dto.FileSize.Should().Be(7);
        dto.StoragePath.Should().Be("tenants/t/staging/g/seed.pdf");

        store.StoreCalls.Should().Be(1);
        store.LastFileName.Should().Be("notes.pdf");
        store.LastContentType.Should().Be("application/pdf");
        store.LastBytes.Should().Equal(System.Text.Encoding.UTF8.GetBytes("PDFDATA"));
    }

    [TestMethod]
    public async Task HandleAsync_ExtensionNotAllowed_RejectedAndStoreNotCalled()
    {
        var store = new FakeFileStore();
        var handler = NewHandler(store);

        var act = async () => await handler.HandleAsync(StageCommand("malware.exe", "application/octet-stream", 10));

        var ex = await act.Should().ThrowAsync<StagedFileRejectedException>();
        ex.Which.IsSizeLimit.Should().BeFalse();
        ex.Which.Message.Should().Contain("'.exe' files are not allowed");
        store.StoreCalls.Should().Be(0, "a rejected payload must never reach the IFileStore");
    }

    [TestMethod]
    public async Task HandleAsync_ExtensionCaseInsensitive_Allowed()
    {
        var store = new FakeFileStore();
        var handler = NewHandler(store);

        var dto = await handler.HandleAsync(StageCommand("SYLLABUS.PDF", "application/pdf", 1024));

        dto.StoragePath.Should().Be("tenants/t/staging/g/seed.pdf",
            "the extension check is case-insensitive so .PDF matches the .pdf allowlist entry");
        store.StoreCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task HandleAsync_SizeCap_Exceeded_RejectedAndStoreNotCalled()
    {
        var store = new FakeFileStore();
        var handler = NewHandler(store,
            Options.Create(new AttachmentUploadOptions { MaxFileSizeBytes = 100 }));

        var act = async () => await handler.HandleAsync(StageCommand("big.pdf", "application/pdf", 101));

        var ex = await act.Should().ThrowAsync<StagedFileRejectedException>();
        ex.Which.IsSizeLimit.Should().BeTrue(
            "size-limit rejections carry IsSizeLimit = true so the endpoint maps them to 413");
        ex.Which.Message.Should().Contain("100-byte per-file limit");
        store.StoreCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task HandleAsync_SizeCap_AtLimit_Accepted()
    {
        var store = new FakeFileStore();
        var handler = NewHandler(store,
            Options.Create(new AttachmentUploadOptions { MaxFileSizeBytes = 100 }));

        var dto = await handler.HandleAsync(StageCommand("ok.pdf", "application/pdf", 100));

        dto.StoragePath.Should().Be("tenants/t/staging/g/seed.pdf");
        store.StoreCalls.Should().Be(1);
    }

    [TestMethod]
    public async Task HandleAsync_EmptyFile_RejectedAndStoreNotCalled()
    {
        var store = new FakeFileStore();
        var handler = NewHandler(store);

        var act = async () => await handler.HandleAsync(StageCommand("empty.pdf", "application/pdf", 0));

        var ex = await act.Should().ThrowAsync<StagedFileRejectedException>();
        ex.Which.IsSizeLimit.Should().BeFalse();
        ex.Which.Message.Should().Contain("file is empty");
        store.StoreCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task HandleAsync_BlankFileName_RejectedAndStoreNotCalled()
    {
        var store = new FakeFileStore();
        var handler = NewHandler(store);

        var act = async () => await handler.HandleAsync(StageCommand("   ", "application/pdf", 10));

        var ex = await act.Should().ThrowAsync<StagedFileRejectedException>();
        ex.Which.IsSizeLimit.Should().BeFalse();
        ex.Which.Message.Should().Contain("file name with an extension");
        store.StoreCalls.Should().Be(0);
    }

    [TestMethod]
    public async Task HandleAsync_BlankContentType_RejectedAndStoreNotCalled()
    {
        var store = new FakeFileStore();
        var handler = NewHandler(store);

        var act = async () => await handler.HandleAsync(StageCommand("notes.pdf", "  ", 10));

        var ex = await act.Should().ThrowAsync<StagedFileRejectedException>();
        ex.Which.IsSizeLimit.Should().BeFalse();
        ex.Which.Message.Should().Contain("content type is missing");
        store.StoreCalls.Should().Be(0);
    }

    // ── R3 (D7/P1-4): extraction runs on stage and never fails the upload ──

    [TestMethod]
    public async Task HandleAsync_ExtractionRunsOnStage_AndItsOutcomeRidesTheResponse()
    {
        var store = new FakeFileStore();
        var extractor = new FakeExtractor
        {
            Next = AttachmentExtractionResult.Succeeded("photosynthesis needs light", 25),
        };
        var handler = NewHandler(store, extractor: extractor);

        var dto = await handler.HandleAsync(StageCommand("notes.pdf", "application/pdf", 9));

        // D7: the extraction happens inside the SAME request, off the bytes read back from the store.
        extractor.ExtractCalls.Should().Be(1, "extraction is triggered on stage, not lazily on generate");
        extractor.LastFileName.Should().Be("notes.pdf");
        extractor.LastFileSize.Should().Be(9);
        store.OpenReadCalls.Should().Be(1, "the extractor reads the stored blob back by storage path");
        store.LastOpenPath.Should().Be(store.StoredPath);

        dto.ExtractionStatus.Should().Be(AttachmentExtractionStatusDto.Succeeded);
        dto.ExtractedText.Should().Be("photosynthesis needs light");
        dto.ExtractionError.Should().BeNull();
        dto.ExtractedAt.Should().NotBeNull("an attempt happened, so the outcome is timestamped");
    }

    [TestMethod]
    public async Task HandleAsync_ExtractionUnsupported_UploadStillSucceedsWithAStatus()
    {
        var store = new FakeFileStore();
        var extractor = new FakeExtractor
        {
            Next = AttachmentExtractionResult.Unsupported("'.png' files are not extracted (PDF and DOCX only)."),
        };
        var handler = NewHandler(store, extractor: extractor);

        var dto = await handler.HandleAsync(StageCommand("photo.png", "image/png", 9));

        // Fail-open (D7): the stage SUCCEEDS and the author is told why there is no text.
        dto.StoragePath.Should().Be(store.StoredPath);
        dto.ExtractionStatus.Should().Be(AttachmentExtractionStatusDto.Unsupported);
        dto.ExtractedText.Should().BeNull();
        dto.ExtractionError.Should().Contain("not extracted");
    }

    [TestMethod]
    public async Task HandleAsync_ExtractorThrows_UploadStillSucceedsAsFailed()
    {
        var store = new FakeFileStore();
        var extractor = new FakeExtractor { ThrowOnExtract = new InvalidOperationException("parser exploded") };
        var handler = NewHandler(store, extractor: extractor);

        var dto = await handler.HandleAsync(StageCommand("notes.pdf", "application/pdf", 9));

        // Defence in depth over the extractor's own fail-open contract.
        dto.StoragePath.Should().Be(store.StoredPath);
        dto.ExtractionStatus.Should().Be(AttachmentExtractionStatusDto.Failed);
        dto.ExtractionError.Should().Be("The file could not be read after upload.");
    }

    [TestMethod]
    public async Task HandleAsync_StoredBlobDisappearsBeforeTheRead_UploadStillSucceedsAsFailed()
    {
        var store = new FakeFileStore { OpenReadThrows = new FileNotFoundException("gone") };
        var handler = NewHandler(store, extractor: new FakeExtractor());

        var dto = await handler.HandleAsync(StageCommand("notes.pdf", "application/pdf", 9));

        dto.StoragePath.Should().Be(store.StoredPath);
        dto.ExtractionStatus.Should().Be(AttachmentExtractionStatusDto.Failed);
    }
}
