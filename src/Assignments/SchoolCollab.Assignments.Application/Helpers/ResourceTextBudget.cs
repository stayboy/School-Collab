using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Helpers;

/// <summary>
/// R3 (P1-6, criterion 6) — the budget the AI generation request's <c>ResourceTexts</c> is filled
/// from. The AI host hard-rejects more than <see cref="MaxResourceTexts"/> entries and any entry
/// longer than <see cref="MaxResourceTextLength"/> with a <c>400</c>
/// (<c>AssignmentQuestionGenerationService.ValidateRequest</c>), and URL texts already consume up to
/// three slots, so attachment texts must <b>share</b> the existing budget rather than extend it —
/// extending it would need an AI-host constant change the round forbids.
/// <para><b>Merge policy:</b> URL texts first (they are author-chosen and already capped at three by
/// the caller), then attachment texts filling the remaining slots, each truncated to the length cap,
/// with a per-attachment fail-open <b>drop</b> once the slots are gone. The drop is never silent:
/// <see cref="ComposeWithReport"/> reports how many attachments it cost so the calling surface can say
/// so (R3-2), mirroring the URL half's warning.</para>
/// </summary>
public static class ResourceTextBudget
{
    /// <summary>Mirrors <c>AssignmentQuestionGenerationService.MaxResourceTexts</c> on the AI host.
    /// Kept in lock-step here so the client never composes a request the AI host would reject.</summary>
    public const int MaxResourceTexts = 5;

    /// <summary>Mirrors <c>AssignmentQuestionGenerationService.MaxResourceTextLength</c>.</summary>
    public const int MaxResourceTextLength = 20000;

    /// <summary>
    /// Composes the request's <c>ResourceTexts</c>, or <c>null</c> when no text qualifies — the same
    /// wire value the pre-R3 composer produced for a URL-only (or empty) selection, so a request with
    /// no attachment text is byte-equivalent to today's.
    /// </summary>
    public static IReadOnlyList<string>? Compose(
        IReadOnlyList<string>? urlTexts,
        IReadOnlyList<string>? attachmentTexts) =>
        ComposeWithReport(urlTexts, attachmentTexts).Texts;

    /// <summary>
    /// <see cref="Compose"/> plus the count of attachment texts the full budget could not carry, so the
    /// surface that owns the generation can tell the author which attachments did not ground it. The
    /// per-attachment drop itself stays fail-open; only its <b>visibility</b> is new.
    /// </summary>
    public static ResourceTextBudgetResult ComposeWithReport(
        IReadOnlyList<string>? urlTexts,
        IReadOnlyList<string>? attachmentTexts)
    {
        var budget = new List<string>(MaxResourceTexts);
        Fill(budget, urlTexts);
        var droppedAttachmentCount = FillAttachments(budget, attachmentTexts);
        return new ResourceTextBudgetResult(budget.Count == 0 ? null : budget, droppedAttachmentCount);
    }

    /// <summary>Adds what still fits. A text over the length cap is truncated (never the other way
    /// round: the extractor already bounded what it produced, so this only guards a hand-authored
    /// URL).</summary>
    private static void Fill(List<string> budget, IReadOnlyList<string>? texts)
    {
        if (texts is null)
        {
            return;
        }

        foreach (var text in texts)
        {
            if (budget.Count >= MaxResourceTexts)
            {
                // Fail-open drop: the remaining texts simply do not ground this generation.
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            budget.Add(text.Length <= MaxResourceTextLength ? text : text[..MaxResourceTextLength]);
        }
    }

    /// <summary>Adds the attachment texts that still fit and returns how many were skipped because the
    /// budget was already full. A null or empty text was never a candidate for a slot, so it is not
    /// reported as dropped — only text that would have grounded the generation had a slot been free.</summary>
    private static int FillAttachments(List<string> budget, IReadOnlyList<string>? texts)
    {
        if (texts is null)
        {
            return 0;
        }

        var dropped = 0;
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (budget.Count >= MaxResourceTexts)
            {
                // Fail-open drop: this attachment simply does not ground the generation. Counted (unlike
                // the URL half) so the UI can surface it instead of letting the row read as if it had
                // been used.
                dropped++;
                continue;
            }

            budget.Add(text.Length <= MaxResourceTextLength ? text : text[..MaxResourceTextLength]);
        }

        return dropped;
    }
}

/// <summary>
/// What <see cref="ResourceTextBudget.ComposeWithReport"/> produced: the request's <c>ResourceTexts</c>
/// (or <c>null</c> when nothing qualified) and how many attachment texts the five-slot budget could not
/// carry. Reported from the budget itself so the UI never re-derives the arithmetic.
/// </summary>
public readonly record struct ResourceTextBudgetResult(IReadOnlyList<string>? Texts, int DroppedAttachmentCount);
