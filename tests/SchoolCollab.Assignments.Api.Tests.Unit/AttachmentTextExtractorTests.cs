using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Assignments.Core.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using PdfPageSize = UglyToad.PdfPig.Content.PageSize;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// R3 (acceptance criteria 1 and 2) — <see cref="AttachmentTextExtractor"/>. Every test here fails
/// against <c>0c8912da</c>, where no extractor exists at all: the class under test, the status enum
/// and <see cref="AttachmentExtractionLimits"/> are all introduced by this round.
/// <para>The inputs are built in-process with the same two libraries the extractor reads (a real
/// <c>%PDF-</c> document via PdfPig's writer, a real OPC package via OpenXml), so the happy paths
/// exercise genuine format parsing rather than a fixture that could drift from the format.</para>
/// </summary>
[TestClass]
public class AttachmentTextExtractorTests
{
    private static AttachmentTextExtractor NewExtractor(int timeoutSeconds = AttachmentExtractionLimits.TimeoutSeconds) =>
        new(Options.Create(new AttachmentExtractionOptions { TimeoutSeconds = timeoutSeconds }),
            NullLogger<AttachmentTextExtractor>.Instance);

    // ── Builders ────────────────────────────────────────────────────────

    private static byte[] BuildPdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PdfPageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var y = 800d;
        foreach (var line in lines)
        {
            page.AddText(line, 12, new PdfPoint(25, y), font);
            y -= 20;
        }
        return builder.Build();
    }

    /// <summary>A valid PDF with one page and NO text — the image-only / scanned shape of D2.</summary>
    private static byte[] BuildTextlessPdf()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PdfPageSize.A4);
        return builder.Build();
    }

    private static byte[] BuildDocx(params string[] paragraphs)
    {
        using var buffer = new MemoryStream();
        using (var document = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document();
            var body = new Body();
            foreach (var paragraph in paragraphs)
            {
                body.Append(new Paragraph(new Run(new Text(paragraph))));
            }
            main.Document.Append(body);
            main.Document.Save();
        }
        return buffer.ToArray();
    }

    private static MemoryStream Stream(byte[] bytes) => new(bytes);

    // ── Criterion 1: real formats ───────────────────────────────────────

    [TestMethod]
    public async Task ExtractAsync_PdfWithAKnownTextLayer_ReturnsThatText()
    {
        var pdf = BuildPdf("Photosynthesis converts light into chemical energy.");

        var result = await NewExtractor().ExtractAsync(Stream(pdf), "notes.pdf", pdf.Length);

        result.Status.Should().Be(AttachmentExtractionStatus.Succeeded);
        result.Text.Should().Contain("Photosynthesis").And.Contain("chemical energy");
        result.Error.Should().BeNull();
    }

    [TestMethod]
    public async Task ExtractAsync_DocxWithKnownText_ReturnsThatText()
    {
        var docx = BuildDocx("Photosynthesis converts light into chemical energy.");

        var result = await NewExtractor().ExtractAsync(Stream(docx), "notes.docx", docx.Length);

        result.Status.Should().Be(AttachmentExtractionStatus.Succeeded);
        result.Text.Should().Contain("Photosynthesis").And.Contain("chemical energy");
    }

    [TestMethod]
    public async Task ExtractAsync_TruncatedPdf_FailsOpenInsteadOfThrowing()
    {
        // A real PDF cut off after its header: the format is right, the document is not.
        var pdf = BuildPdf("Body text");
        var truncated = pdf.AsSpan(0, Math.Min(64, pdf.Length)).ToArray();

        var result = await NewExtractor().ExtractAsync(Stream(truncated), "broken.pdf", truncated.Length);

        result.Status.Should().BeOneOf(AttachmentExtractionStatus.Failed, AttachmentExtractionStatus.Unsupported);
        result.Text.Should().BeNull();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task ExtractAsync_PdfWithNoTextLayer_IsUnsupportedNotSilentlyEmpty()
    {
        // D2: image-only / scanned PDFs have no text layer; OCR is out of scope, so the author must be
        // told rather than handed an empty extraction that looks like success.
        var pdf = BuildTextlessPdf();

        var result = await NewExtractor().ExtractAsync(Stream(pdf), "scan.pdf", pdf.Length);

        result.Status.Should().Be(AttachmentExtractionStatus.Unsupported);
        result.Text.Should().BeNull();
        result.Error.Should().Contain("text layer");
    }

    [TestMethod]
    public async Task ExtractAsync_UnsupportedExtension_IsUnsupportedWithoutParsing()
    {
        // A body that is NOT a valid document at all: if the extractor tried to parse it, it would have
        // to fail rather than report "unsupported" — so the status alone proves nothing was read.
        var body = Encoding.UTF8.GetBytes("plain text, not a document");

        var result = await NewExtractor().ExtractAsync(new MemoryStream(body), "notes.txt", body.Length);

        result.Status.Should().Be(AttachmentExtractionStatus.Unsupported);
        result.Error.Should().Contain("'.txt'");
    }

    [TestMethod]
    public async Task ExtractAsync_LegacyOrImageFormats_AreUnsupported()
    {
        var body = Encoding.UTF8.GetBytes("whatever");

        foreach (var name in new[] { "slides.pptx", "photo.png", "old.doc" })
        {
            var result = await NewExtractor().ExtractAsync(new MemoryStream(body), name, body.Length);
            result.Status.Should().Be(
                AttachmentExtractionStatus.Unsupported, $"'{name}' is outside R3's PDF+DOCX scope");
        }
    }

    // ── Criterion 2: safety ─────────────────────────────────────────────

    [TestMethod]
    public async Task ExtractAsync_FileAboveTheExtractionCap_IsUnsupportedAndTheStreamIsNeverRead()
    {
        // Distinct from the pre-existing 25 MiB stage refusal: this file is still STORED, it is just
        // never parsed. The stream throws on the first read, so any attempt to sniff or parse it would
        // surface as a Failed — the Unsupported status is itself the proof that nothing was read.
        var oversized = AttachmentExtractionLimits.MaxInputBytes + 1;

        var result = await NewExtractor().ExtractAsync(
            new ExplodingStream(), "huge.pdf", oversized);

        result.Status.Should().Be(AttachmentExtractionStatus.Unsupported);
        result.Error.Should().Contain("extraction limit");
    }

    [TestMethod]
    public async Task ExtractAsync_FileAtTheExtractionCap_IsStillParsed()
    {
        var pdf = BuildPdf("Body text");

        // Reported exactly at the cap: the guard is "greater than", not "greater or equal".
        var result = await NewExtractor().ExtractAsync(Stream(pdf), "notes.pdf", AttachmentExtractionLimits.MaxInputBytes);

        result.Status.Should().Be(AttachmentExtractionStatus.Succeeded);
    }

    [TestMethod]
    public async Task ExtractAsync_CorrectExtensionButWrongMagicBytes_IsFailed()
    {
        // StagedFileValidator gates by EXTENSION ONLY and never validates ContentType (D8(d)), so the
        // declared name is not evidence. A zip container named ".pdf" must not be trusted.
        var docx = BuildDocx("hidden inside a zip");

        var result = await NewExtractor().ExtractAsync(Stream(docx), "actually-a-docx.pdf", docx.Length);

        result.Status.Should().Be(AttachmentExtractionStatus.Failed);
        result.Text.Should().BeNull();

        // The inverse mismatch: a PDF named ".docx".
        var pdf = BuildPdf("Body text");
        var inverse = await NewExtractor().ExtractAsync(Stream(pdf), "actually-a-pdf.docx", pdf.Length);
        inverse.Status.Should().Be(AttachmentExtractionStatus.Failed);
    }

    [TestMethod]
    public async Task ExtractAsync_DocumentFarLargerThanTheCeiling_StopsReadingAtTheCeiling()
    {
        // D8(b): the whole point of streaming is that a decompression bomb is never materialised. The
        // document below holds ~400 000 characters — twenty times the ceiling — so an
        // extract-then-truncate implementation would have to observe all 400 000 before trimming.
        const int paragraphCount = 4000;
        const int paragraphLength = 100;
        var paragraphs = Enumerable
            .Range(0, paragraphCount)
            .Select(i => new string((char)('a' + (i % 26)), paragraphLength))
            .ToArray();
        var docx = BuildDocx(paragraphs);

        var stopwatch = Stopwatch.StartNew();
        var result = await NewExtractor().ExtractAsync(Stream(docx), "bomb.docx", docx.Length);
        stopwatch.Stop();

        result.Status.Should().Be(AttachmentExtractionStatus.Succeeded);
        result.Text!.Length.Should().Be(
            AttachmentExtractionLimits.MaxCharacters, "the retained text is exactly the ceiling");

        // The work-done assertion: the reader observed ~one ceiling, not 400 000 characters.
        result.ScannedCharacters.Should().BeLessThan(
            2 * AttachmentExtractionLimits.MaxCharacters,
            "the reader must stop AT the ceiling, not materialise the document and then truncate");
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(30),
            "a bomb that was actually materialised would not finish this quickly");
    }

    [TestMethod]
    public async Task ExtractAsync_PdfBeyondTheCeiling_AlsoStopsAtTheCeiling()
    {
        // Same contract on the PDF leg: many pages, each with text, and a ceiling far below the total.
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var pageIndex = 0; pageIndex < 40; pageIndex++)
        {
            var page = builder.AddPage(PdfPageSize.A4);
            for (var line = 0; line < 25; line++)
            {
                page.AddText(new string('x', 60), 10, new PdfPoint(25, 800 - (line * 25)), font);
            }
        }
        var pdf = builder.Build();

        var result = await NewExtractor().ExtractAsync(Stream(pdf), "long.pdf", pdf.Length);

        result.Status.Should().Be(AttachmentExtractionStatus.Succeeded);
        result.Text!.Length.Should().BeLessThanOrEqualTo(AttachmentExtractionLimits.MaxCharacters);
        result.ScannedCharacters.Should().BeLessThanOrEqualTo(
            AttachmentExtractionLimits.MaxCharacters + 100,
            "the page enumeration is lazy, so reading stops at the ceiling rather than at the last page");
    }

    [TestMethod]
    public async Task ExtractAsync_ErrorIsBoundedToTheColumnCap()
    {
        // D8(c): the error is persisted, so it must fit the column it is persisted into.
        var junk = Encoding.UTF8.GetBytes("%PDF-" + new string('J', 50_000));

        var result = await NewExtractor().ExtractAsync(new MemoryStream(junk), "junk.pdf", junk.Length);

        result.Status.Should().BeOneOf(AttachmentExtractionStatus.Failed, AttachmentExtractionStatus.Unsupported);
        result.Error!.Length.Should().BeLessThanOrEqualTo(AttachmentExtractionLimits.MaxErrorLength);
    }

    [TestMethod]
    public async Task ExtractAsync_ParserThatNeverReturns_FailsOpenAtTheTimeoutInsteadOfHangingTheRequest()
    {
        // D8(a): the per-file wall clock. Neither PdfPig nor OpenXml accepts a cancellation token, so the
        // budget is enforced by racing the parse — this pins that a hostile/slow file cannot hold the
        // stage request open. The stream blocks until released in the finally block, so the abandoned
        // parse thread does not leak into the rest of the run.
        var blocker = new ManualResetEventSlim(false);
        var stalling = new BlockingStream(PdfMagic(), blocker);
        var extractor = NewExtractor(timeoutSeconds: 1);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await extractor.ExtractAsync(
                stalling, "slow.pdf", 1024, CancellationToken.None);
            stopwatch.Stop();

            result.Status.Should().Be(AttachmentExtractionStatus.Failed);
            result.Error.Should().Contain("timed out");
            stopwatch.Elapsed.Should().BeLessThan(
                TimeSpan.FromSeconds(20), "the budget is 1s — a hang would not return here at all");
        }
        finally
        {
            blocker.Set();
        }
    }

    [TestMethod]
    public async Task ExtractAsync_CallerCancels_PropagatesInsteadOfReportingATimeout()
    {
        // EC-2: navigation cancellation is not an extraction outcome.
        var blocker = new ManualResetEventSlim(false);
        var stalling = new BlockingStream(PdfMagic(), blocker);
        var extractor = NewExtractor(timeoutSeconds: 30);
        using var cts = new CancellationTokenSource();
        try
        {
            var act = async () => await extractor.ExtractAsync(
                stalling, "slow.pdf", 1024, cts.Token);
            var pending = act();
            cts.CancelAfter(200);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            blocker.Set();
        }
    }

    private static byte[] PdfMagic() => "%PDF-"u8.ToArray();

    /// <summary>A stream whose read never completes until the test releases it — the slow/hostile file.</summary>
    private sealed class BlockingStream(byte[] header, ManualResetEventSlim gate) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => header.Length;
        public override long Position { get; set; }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            gate.Wait();
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = offset;
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A stream that fails loudly on any read — the "nothing was read" probe for the size cap.</summary>
    private sealed class ExplodingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("The stream was read, but the size cap should have short-circuited.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
