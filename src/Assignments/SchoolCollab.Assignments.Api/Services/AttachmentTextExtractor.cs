using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchoolCollab.Assignments.Core.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using WordprocessingText = DocumentFormat.OpenXml.Wordprocessing.Text;
using WordprocessingParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// R3 (assignment-authoring-compartments §14 / D1/D2/D8) — the real
/// <see cref="IAttachmentTextExtractor"/>: <c>PdfPig</c> for PDF and
/// <c>DocumentFormat.OpenXml</c> for DOCX, both read <b>streaming and early-aborting</b> at
/// <see cref="AttachmentExtractionLimits.MaxCharacters"/>.
/// <para><b>Why streaming and not extract-then-truncate:</b> a 25 MiB DOCX is a zip whose
/// decompressed size is unbounded, so materialising the whole document before applying the
/// ceiling is exactly the decompression-bomb amplification D8(b) exists to prevent. The DOCX
/// path therefore uses <see cref="OpenXmlReader"/> (SAX — one element at a time) and the PDF path
/// enumerates <c>GetPages()</c> lazily, both breaking out <i>at</i> the ceiling.</para>
/// <para><b>Why the header is sniffed:</b> <c>StagedFileValidator</c> gates on the file
/// <b>extension only</b> — <c>ContentType</c> is never validated — so the declared name is not
/// evidence of the payload. The real first bytes decide (D8(d)) and a mismatch fails closed.</para>
/// <para><b>Package identity:</b> the reader is the NuGet id <c>PdfPig</c> (authors
/// <c>UglyToad</c>, Apache-2.0). The similarly-spelled <c>UglyToad.PdfPig</c> id is an unrelated
/// two-version placeholder with no licence and no project url — do not "correct" it.</para>
/// </summary>
public sealed class AttachmentTextExtractor(IOptions<AttachmentExtractionOptions> options, ILogger<AttachmentTextExtractor> logger)
    : IAttachmentTextExtractor
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    /// <inheritdoc />
    public async Task<AttachmentExtractionResult> ExtractAsync(
        Stream content,
        string fileName,
        long fileSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

        // D2: PDF + DOCX only. Everything else (legacy binary .doc, Office Open XML formats we do
        // not read, images, plain text) is out of scope and reported as such rather than dropped.
        if (extension is not (".pdf" or ".docx"))
        {
            return AttachmentExtractionResult.Unsupported(
                $"'{extension}' files are not extracted (PDF and DOCX only).");
        }

        // D8(c): the extraction-specific cap. Distinct from — and below — the stage refusal
        // (AttachmentUploadOptions.MaxFileSizeBytes, 25 MiB): this file is stored, never parsed.
        if (fileSize > AttachmentExtractionLimits.MaxInputBytes)
        {
            return AttachmentExtractionResult.Unsupported(
                $"The file exceeds the {AttachmentExtractionLimits.MaxInputBytes / (1024 * 1024)} MB extraction limit.");
        }

        // D8(a): the per-file wall clock. A linked source so the caller's token and the timeout are
        // one signal; the timeout is enforced by racing the parse below, because neither PdfPig nor
        // OpenXml accepts a cancellation token.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_timeout);

        var parse = Task.Run(() => ExtractCore(content, extension), CancellationToken.None);
        var timeout = Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);

        var winner = await Task.WhenAny(parse, timeout);
        if (winner != parse)
        {
            // Distinguish "the caller navigated away" from "this file is too slow to read".
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogWarning(
                "Extraction of {FileName} exceeded the {Seconds}s budget", fileName, _timeout.TotalSeconds);
            return AttachmentExtractionResult.Failed(
                $"Extraction timed out after {_timeout.TotalSeconds:0.#} seconds.");
        }

        try
        {
            return await parse;
        }
        catch (Exception ex)
        {
            // Fail-open is the contract: a malformed or hostile file never fails the upload (D7).
            logger.LogWarning(ex, "Extraction of {FileName} failed", fileName);
            return AttachmentExtractionResult.Failed(Bound($"The file could not be read: {ex.Message}"));
        }
    }

    /// <summary>
    /// The synchronous read. Runs on a pool thread so the timeout above can abandon it; every
    /// return path is a non-throwing <see cref="AttachmentExtractionResult"/>.
    /// </summary>
    private AttachmentExtractionResult ExtractCore(Stream content, string extension)
    {
        if (!content.CanSeek)
        {
            // Sniffing rewinds, and both readers want a seekable source. The caller has already
            // cleared the size cap, so this copy is bounded at MaxInputBytes.
            var buffered = new MemoryStream();
            content.CopyTo(buffered);
            buffered.Position = 0;
            content = buffered;
        }

        var declared = extension == ".pdf" ? PdfMagic : ZipMagic;
        var actual = ReadHeader(content, declared.Length);
        if (actual is null || !actual.AsSpan().SequenceEqual(declared))
        {
            // D8(d): the extension claims a format the bytes do not back. Never trust it.
            return AttachmentExtractionResult.Failed(
                $"The file does not look like a valid {extension[1..].ToUpperInvariant()} document.");
        }

        content.Position = 0;
        return extension == ".pdf" ? ExtractPdf(content) : ExtractDocx(content);
    }

    private static byte[]? ReadHeader(Stream content, int length)
    {
        var buffer = new byte[length];
        content.Position = 0;
        var read = content.ReadAtLeast(buffer, length, throwOnEndOfStream: false);
        content.Position = 0;
        return read == length ? buffer : null;
    }

    /// <summary>
    /// PdfPig, page by page (D8(b)). <c>GetPages()</c> is lazy, so a document that reaches the
    /// ceiling on page 2 never parses page 3.
    /// </summary>
    private AttachmentExtractionResult ExtractPdf(Stream content)
    {
        using var document = PdfDocument.Open(content, new ParsingOptions
        {
            UseLenientParsing = true,
            SkipMissingFonts = true,
        });

        var builder = new StringBuilder(AttachmentExtractionLimits.MaxCharacters);
        var scanned = 0;

        foreach (var page in document.GetPages())
        {
            foreach (var word in page.GetWords())
            {
                if (string.IsNullOrEmpty(word.Text)) continue;

                scanned += word.Text.Length + 1;
                AppendCapped(builder, word.Text);
                AppendCapped(builder, " ");
                if (scanned >= AttachmentExtractionLimits.MaxCharacters) break;
            }

            if (scanned >= AttachmentExtractionLimits.MaxCharacters) break;
        }

        if (builder.Length == 0)
        {
            // D2: an image-only (scanned) PDF has no text layer; OCR is explicitly out of R3.
            return AttachmentExtractionResult.Unsupported(
                "No text layer was found (scanned images are not read).");
        }

        return AttachmentExtractionResult.Succeeded(builder.ToString().Trim(), scanned);
    }

    /// <summary>
    /// OpenXml in SAX mode (D8(b)): one element at a time through <see cref="OpenXmlReader"/>, so
    /// the decompressed document is never held whole. Breaks at the ceiling.
    /// </summary>
    private AttachmentExtractionResult ExtractDocx(Stream content)
    {
        using var document = WordprocessingDocument.Open(content, false);
        var main = document.MainDocumentPart;
        if (main?.Document is null)
        {
            return AttachmentExtractionResult.Failed("The DOCX has no main document part.");
        }

        var builder = new StringBuilder(AttachmentExtractionLimits.MaxCharacters);
        var scanned = 0;

        using var reader = OpenXmlReader.Create(main);
        while (reader.Read())
        {
            if (reader.ElementType == typeof(WordprocessingText) && !reader.IsEndElement)
            {
                var text = reader.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    scanned += text.Length;
                    AppendCapped(builder, text);
                    if (scanned >= AttachmentExtractionLimits.MaxCharacters) break;
                }
            }
            else if (reader.ElementType == typeof(WordprocessingParagraph) && reader.IsEndElement)
            {
                AppendCapped(builder, "\n");
            }
        }

        if (builder.Length == 0)
        {
            return AttachmentExtractionResult.Unsupported(
                "No text was found in the document.");
        }

        return AttachmentExtractionResult.Succeeded(builder.ToString().Trim(), scanned);
    }

    /// <summary>Appends only what still fits the retained ceiling, so the buffer is bounded
    /// <b>by construction</b> rather than by a later truncation pass.</summary>
    private static void AppendCapped(StringBuilder builder, string value)
    {
        var remaining = AttachmentExtractionLimits.MaxCharacters - builder.Length;
        if (remaining <= 0) return;
        builder.Append(value.Length <= remaining ? value : value[..remaining]);
    }

    /// <summary>Keeps the persisted/returned error inside the column cap (D8(c)).</summary>
    private static string Bound(string message)
    {
        return message.Length <= AttachmentExtractionLimits.MaxErrorLength
            ? message
            : message[..(AttachmentExtractionLimits.MaxErrorLength - 1)] + "…";
    }
}
