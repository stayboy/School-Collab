using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// C3 — QuestPDF (pin 2026.8.0) renderer for the sign-off certificate
/// (decision (a), amendment A-1). Lives in the Assignments.Api delivery layer
/// (NOT the domain project, so QuestPDF stays out of Assignments.Core — the
/// plan's decision-(a) intent) and is the only runtime that dispatches
/// finalize, so the API host can resolve it while the Blazor Application RCL
/// cannot be seen by the API. Implements <see cref="IAssignmentCertificateGenerator"/>.
/// Layout v1: a single English A4 page with a modest title header; a
/// student/guardian/assignment block; a signature block (type + typed name);
/// the consent text shown at signing (verbatim audit value) in a footer; and a
/// generated-at page footer. No logos / images.
/// </summary>
public sealed class AssignmentCertificateGenerator(
    ILogger<AssignmentCertificateGenerator> logger) : IAssignmentCertificateGenerator
{
    static AssignmentCertificateGenerator()
    {
        // QuestPDF community license (org < $1M annual revenue) — set once via
        // the static ctor so the renderer is idempotent across instantiations.
        // The 2026.8.0 pin stays fixed (2026.9.0 introduced font-system
        // breaking changes); the default Lato font is bundled and fine for the
        // English v1 layout.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<byte[]> GenerateAsync(AssignmentCertificateContent content, CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Generating certificate for assignment {AssignmentTitle} / student {StudentName}",
            content.AssignmentTitle, content.StudentName);

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(style => style.FontSize(11));

                page.Header()
                    .PaddingBottom(16)
                    .BorderBottom(1)
                    .BorderColor(Colors.Grey.Lighten2)
                    .Text("Sign-off Certificate")
                    .FontSize(20).Bold().FontColor("#1f3a5f");

                page.Content().Column(column =>
                {
                    column.Spacing(8);

                    column.Item().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(11).SemiBold());
                        text.Span("Student (ward)");
                    });

                    column.Item().Text(content.StudentName);

                    column.Item().PaddingTop(6).Text(text =>
                    {
                        text.Span("Guardian signer: ").SemiBold();
                        text.Span(content.SignerGuardianName);
                    });
                    column.Item().Text(text =>
                    {
                        text.Span("Assignment: ").SemiBold();
                        text.Span(content.AssignmentTitle);
                    });
                    column.Item().Text(text =>
                    {
                        text.Span("Signed on: ").SemiBold();
                        text.Span(content.SignedAt.ToLocalTime().ToString("g"));
                    });
                    column.Item().Text(text =>
                    {
                        text.Span("Finalized on: ").SemiBold();
                        text.Span(content.FinalizedAt.ToLocalTime().ToString("g"));
                    });

                    column.Item().PaddingTop(6).Text(text =>
                    {
                        text.Span("Signature method: ").SemiBold();
                        text.Span(content.SignatureType == SignatureType.Typed ? "Typed" : "Click");
                    });
                    if (content.SignatureType == SignatureType.Typed)
                    {
                        column.Item().Text(text =>
                        {
                            text.Span("Typed name: ").SemiBold();
                            text.Span(content.TypedSignature);
                        });
                    }

                    column.Item().PaddingTop(10).Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(9).FontColor(Colors.Grey.Darken1));
                        text.Span("Consent text shown at signing:");
                    });
                    column.Item().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(9).FontColor(Colors.Grey.Darken1));
                        text.Span(content.ConsentTextShown);
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(9).FontColor(Colors.Grey.Darken1));
                    text.Span($"Generated on {DateTimeOffset.UtcNow.ToLocalTime():g}");
                });
            });
        }).GeneratePdf();

        logger.LogInformation(
            "Generated {Bytes} certificate bytes for assignment {AssignmentTitle} / student {StudentName}",
            pdf.Length, content.AssignmentTitle, content.StudentName);

        return Task.FromResult(pdf);
    }
}
