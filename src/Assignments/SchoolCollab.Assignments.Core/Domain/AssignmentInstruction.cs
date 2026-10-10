namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// QR-5 / §5.6 (owner, 2026-10-09): one instruction block — the teacher's guidance to the student.
/// <b>One shape, two owners:</b> <see cref="QuestionId"/> null means the instruction belongs to the
/// ASSIGNMENT itself, a non-null value ties it to that question.
///
/// <para>Ownership sits with the <see cref="Assignment"/> aggregate (one table, one child
/// collection) even for question-owned rows, mirroring how attachments and questions themselves are
/// owned; the read handlers group by <see cref="QuestionId"/>. The row deliberately carries no
/// <c>TenantId</c> of its own, like every other assignment child — the tenant is the assignment's.</para>
///
/// <para>Text and URL instructions carry <see cref="Text"/>/<see cref="Url"/>; Audio, Video and Image
/// instructions carry the metadata of a file already staged to storage (the
/// <see cref="AssignmentAttachment"/> contract, minus extraction — instructions are authored, never
/// parsed for grounding).</para>
/// </summary>
public sealed class AssignmentInstruction
{
    private AssignmentInstruction() { }

    internal AssignmentInstruction(
        Guid assignmentId,
        Guid? questionId,
        InstructionKind kind,
        string? text,
        string? url,
        string? fileName,
        string? contentType,
        long fileSize,
        string? storagePath,
        int displayOrder,
        /// <summary>Round <c>instructional-materials</c> (D6/D7): the material's NAME — required for
        /// a Text row (the validator's rule), an optional label for a Link, unused by the media kinds.
        /// Trailing and defaulted deliberately (Q2, 2026-10-10): the three aggregate constructions keep
        /// compiling while the setters are threaded one batch at a time, and the per-path round-trip
        /// tests — not the compiler — are the gate against a site that silently persists null.</summary>
        string? title = null)
    {
        Id = Guid.NewGuid();
        AssignmentId = assignmentId;
        QuestionId = questionId;
        Kind = kind;
        Text = text;
        Url = url;
        FileName = fileName;
        ContentType = contentType;
        FileSize = fileSize;
        StoragePath = storagePath;
        DisplayOrder = displayOrder;
        Title = title;
    }

    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }

    /// <summary>Null = the assignment's own instruction; non-null = that question's. This is the
    /// owner discriminator the whole feature turns on (§5.6).</summary>
    public Guid? QuestionId { get; private set; }

    public InstructionKind Kind { get; private set; }

    /// <summary>The prose for <see cref="InstructionKind.Text"/>.</summary>
    public string? Text { get; private set; }

    /// <summary>The target for <see cref="InstructionKind.Url"/>.</summary>
    public string? Url { get; private set; }

    /// <summary>Media metadata for Audio/Video/Image; null for Text/Url.</summary>
    public string? FileName { get; private set; }
    public string? ContentType { get; private set; }
    public long FileSize { get; private set; }

    /// <summary>Opaque storage key for a staged media instruction (the attachment precedent).</summary>
    public string? StoragePath { get; private set; }

    /// <summary>Round <c>instructional-materials</c> (D6/D7): the material's name — required for a Text
    /// row, an optional label for a Link, unused by the media kinds. Nullable because rows written
    /// before the column carry none, and nothing invents one for them.</summary>
    public string? Title { get; private set; }

    /// <summary>The author's order within its owner (re-indexed 0..n-1 on every write).</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>Whether the row carries a staged media file rather than text or a link — the single
    /// source the validator and the UI both read (never re-derive from <see cref="Kind"/> elsewhere).</summary>
    public bool HasMedia =>
        Kind is InstructionKind.Audio or InstructionKind.Video or InstructionKind.Image;
}
