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

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static StageAttachmentCommandHandler NewHandler(
        IFileStore fileStore,
        IOptions<AttachmentUploadOptions>? uploadOptions = null)
    {
        return new StageAttachmentCommandHandler(
            fileStore,
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
}
