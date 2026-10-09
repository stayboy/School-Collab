using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data.Configurations;

internal sealed class AssignmentConfiguration : TenantEntityTypeConfigurationBase<Assignment>
{
    public AssignmentConfiguration(Expression<Func<Guid>> tenantIdAccessor) : base(tenantIdAccessor) { }

    protected override void ConfigureTenantEntity(EntityTypeBuilder<Assignment> builder)
    {
        builder.ToTable("assignments");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(5000);

        // INS-1 (assignment-authoring-compartments §9): student-facing task text,
        // distinct from Description (the internal/author summary).
        builder.Property(x => x.Instructions)
            .HasMaxLength(4000);

        builder.Property(x => x.AssignmentType)
            .IsRequired()
            .HasDefaultValue(AssignmentType.Digital);

        builder.Property(x => x.GradingFormat)
            .IsRequired()
            .HasDefaultValue(GradingFormat.TeacherGraded);

        builder.Property(x => x.TargetAudienceType)
            .IsRequired()
            .HasDefaultValue(TargetAudienceType.AllStudents);

        builder.Property(x => x.TopicId);
        builder.Property(x => x.AssignmentNumber).HasMaxLength(50);

        builder.Property(x => x.DueDate);

        builder.Property(x => x.MaxScore)
            .HasPrecision(5, 2);

        // ── WS-A3 (spec §3.3 + §7 Q4): pass score + max attempts ────────
        // PassScore is the threshold at which a submission is considered
        // passed (null = no pass/fail signal). MaxAttempts is the cap on
        // submission attempts (null = unlimited; >= 1 when set). Both
        // are draft-only fields (set at create / update) — mirroring the
        // MaxScore posture above.
        builder.Property(x => x.PassScore)
            .HasPrecision(5, 2);
        builder.Property(x => x.MaxAttempts);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasDefaultValue(AssignmentStatus.Draft);

        builder.Property(x => x.CreatedByTeacherId)
            .IsRequired();

        builder.Property(x => x.MandatoryReview)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.PublishedAt);

        builder.Property(x => x.AiPromptOverride).HasMaxLength(4000);

        // ── WS-A2 lifecycle (spec §3.5 step 2 / §7 Q2 / Q6) ─────────────────
        // Scheduled auto-publish window + per-row archive grace + approval
        // workflow (nullable ApprovalStatus = not yet submitted). The
        // ArchiveGraceDays default mirrors the spec's 30-day retention
        // floor; the EF default ensures existing rows land on a sane value
        // when the migration backfills NULLs.
        builder.Property(x => x.AvailableFromUtc);
        builder.Property(x => x.ArchiveGraceDays).HasDefaultValue(30);
        builder.Property(x => x.ApprovalStatus);
        builder.Property(x => x.ApprovedBy);
        builder.Property(x => x.ApprovedAt);

        // ── R4 (CP-5/D23): the authoring context picks ─────────────────────────
        // Two Npgsql first-class `uuid[]` arrays (no owned entity, no converter, no join
        // table) — the picks are an opaque id set with no per-row metadata, and a join
        // table's only real advantage (referential integrity) is impossible here because the
        // strands/lessons live in the Students context's database.
        //
        // NOT NULL with an empty-array default: the CLR properties are non-nullable, so EF
        // marks the columns required, and the default backfills every existing row in the one
        // additive ADD COLUMN — a NOT NULL column with no default would fail on a non-empty
        // table. "No picks" therefore has exactly one representation, the empty array: the
        // property initializer runs at construction only, so a NULL column value would not
        // read back as empty. No index: the picks feed no query path (they ride the single-row
        // authoring-children read), and one would be speculative scaffolding.
        builder.Property(x => x.ContextStrandIds)
            .HasColumnType("uuid[]")
            .HasDefaultValue(Array.Empty<Guid>());
        builder.Property(x => x.ContextLessonIds)
            .HasColumnType("uuid[]")
            .HasDefaultValue(Array.Empty<Guid>());


        builder.HasIndex(x => x.TopicId)
            .HasDatabaseName("ix_assignments_topic_id");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("ix_assignments_status");

        builder.HasIndex(x => x.CreatedByTeacherId)
            .HasDatabaseName("ix_assignments_teacher_id");

        builder.Ignore(x => x.DomainEvents);

        builder.Navigation(x => x.Questions).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.Navigation(x => x.Reviews).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.Navigation(x => x.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        // WS-A1: standalone child collections — FK declared once from the
        // aggregate side; Cascade covers assignment deletion; removing from
        // the collection severs the required FK, so EF Core deletes the
        // row at SaveChanges (the update handler's replacement semantics).
        builder.HasMany(x => x.Modules).WithOne().HasForeignKey(m => m.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Modules).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.HasMany(x => x.Resources).WithOne().HasForeignKey(r => r.AssignmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Resources).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        // R2 (TGT-1): the authored targeting rows are a required navigation for publish
        // (the fail-closed resolver reads them off the aggregate) and for the authoring
        // child read, so they are auto-included like the other structural children.
        builder.Navigation(x => x.Targets).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        // ── Owned children: the key MUST be ValueGeneratedNever (R3 scope exception) ──────────
        // Every owned collection below declares `HasKey(x => x.Id)` over a Guid. Without
        // `ValueGeneratedNever()` the Guid key keeps the `ValueGeneratedOnAdd` convention, and EF Core
        // then treats an explicitly-set key on a newly-attached owned instance as an ALREADY EXISTING
        // row: the new element is tracked as `Modified`, so EF emits
        // `UPDATE <child table> SET ... WHERE id = <the new id>` and NO INSERT. On PostgreSQL that
        // affects 0 rows and `SaveChanges` throws `DbUpdateConcurrencyException` — i.e. adding or
        // replacing any question/attachment/option/review on a PERSISTED assignment failed outright.
        // (The root aggregates are immune because EntityTypeConfigurationBase.ConfigureGuidId() calls
        // ValueGeneratedNever() for them.) This was a PRE-EXISTING defect, found by R3's Postgres
        // round-trip tests and fixed here — see §17 of documents/specs/assignment-authoring-compartments.md.
        // Do NOT remove these four calls: they are load-bearing, not stylistic.
        builder.OwnsMany(x => x.Attachments, a =>
        {
            a.ToTable("assignment_attachments");
            a.WithOwner().HasForeignKey(a => a.AssignmentId);
            a.HasKey(a => a.Id);
            a.Property(a => a.Id).ValueGeneratedNever();
            a.Property(a => a.FileName).IsRequired().HasMaxLength(255);
            a.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
            a.Property(a => a.FileSize).IsRequired();
            a.Property(a => a.StoragePath).IsRequired().HasMaxLength(500);
            // R3 (D4/P1-3): the persisted extraction outcome. MaxCharacters mirrors
            // AttachmentExtractionLimits.MaxCharacters — the extractor's own ceiling — so the
            // column can never truncate what the extractor retained.
            a.Property(a => a.ExtractionStatus).IsRequired().HasDefaultValue(AttachmentExtractionStatus.NotAttempted);
            a.Property(a => a.ExtractedText).HasMaxLength(AttachmentExtractionLimits.MaxCharacters);
            a.Property(a => a.ExtractedAt);
            a.Property(a => a.ExtractionError).HasMaxLength(AttachmentExtractionLimits.MaxErrorLength);
        });

        builder.OwnsMany(x => x.Questions, q =>
        {
            q.ToTable("assignment_questions");
            q.WithOwner().HasForeignKey(q => q.AssignmentId);
            q.HasKey(q => q.Id);
            q.Property(q => q.Id).ValueGeneratedNever();
            q.Property(q => q.QuestionText).IsRequired().HasMaxLength(2000);
            q.Property(q => q.QuestionType).IsRequired().HasDefaultValue(QuestionType.MultipleChoice);
            q.Property(q => q.DisplayOrder).IsRequired();
            q.Property(q => q.CorrectOptionId);
            q.Property(q => q.ModelAnswer).HasMaxLength(2000);
            // R3 (D4/P1-2): provenance link to the generation header. Nullable — hand-written
            // questions have none — and deliberately not a required relationship, so an
            // assignment whose generation header was never recorded still round-trips.
            q.Property(q => q.GenerationId);

            // ── D16/QR-2: the response kinds ────────────────────────────────────────────────
            // A small, ordered value set with no per-row metadata, so it is a first-class array
            // column rather than a join table — the ContextStrandIds precedent above.
            //
            // Deliberately NO HasConversion: EF Core's primitive-collection support maps the
            // IReadOnlyList<T> natively and applies the enum's own element conversion to the array
            // (`integer[]`). A hand-written converter COMPOSES with that element conversion and the
            // model build dies with "Cannot compose converter … because the output type of the first
            // converter doesn't match the input type of the second" — so the element-level mapping is
            // EF's job, and the column type is all the configuration this needs.
            //
            // NOT NULL with an empty-array default: the CLR property is non-nullable, so the one
            // additive ADD COLUMN backfills every existing row, and "no kinds recorded" has exactly
            // one representation. The ≥1 rule is enforced on WRITE (QuestionOptionDtoValidator), so
            // a legacy question reads back with an empty set instead of failing.
            q.Property(q => q.ResponseKinds)
                .HasColumnType("integer[]")
                .HasDefaultValue(Array.Empty<ResponseKind>());

            q.OwnsMany(q => q.Options, o =>
            {
                o.ToTable("question_options");
                o.WithOwner().HasForeignKey(o => o.QuestionId);
                o.HasKey(o => o.Id);
                o.Property(o => o.Id).ValueGeneratedNever();
                o.Property(o => o.OptionText).IsRequired().HasMaxLength(500);
                o.Property(o => o.IsCorrect).IsRequired().HasDefaultValue(false);
            });
        });

        // ── QR-5/§5.6 (owner, 2026-10-09): the instruction blocks ──────────────────────────
        // ONE table for both owners: a row with QuestionId null is the ASSIGNMENT's own
        // instruction, a row with an id belongs to that question — the shared shape the spec
        // settled. The collection is owned by the aggregate rather than by the question because
        // an owned type cannot be the principal of another relationship, so the question link is
        // a plain nullable id the aggregate itself keeps consistent (RemoveQuestion purges the
        // rows of a question it drops).
        builder.OwnsMany(x => x.InstructionItems, i =>
        {
            i.ToTable("assignment_instructions");
            i.WithOwner().HasForeignKey(i => i.AssignmentId);
            i.HasKey(i => i.Id);
            i.Property(i => i.Id).ValueGeneratedNever();
            i.Property(i => i.QuestionId);
            i.Property(i => i.Kind).IsRequired().HasDefaultValue(InstructionKind.Text);
            i.Property(i => i.Text).HasMaxLength(4000);
            i.Property(i => i.Url).HasMaxLength(2000);
            i.Property(i => i.FileName).HasMaxLength(255);
            i.Property(i => i.ContentType).HasMaxLength(100);
            i.Property(i => i.FileSize).IsRequired();
            i.Property(i => i.StoragePath).HasMaxLength(500);
            i.Property(i => i.DisplayOrder).IsRequired();
            i.HasIndex(i => i.QuestionId).HasDatabaseName("ix_assignment_instructions_question_id");
        });
        builder.Navigation(x => x.InstructionItems).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.OwnsMany(x => x.Reviews, r =>
        {
            r.ToTable("assignment_reviews");
            r.WithOwner().HasForeignKey(r => r.AssignmentId);
            r.HasKey(r => r.Id);
            r.Property(r => r.Id).ValueGeneratedNever();
            r.Property(r => r.TeacherId).IsRequired();
            r.Property(r => r.Score).HasPrecision(5, 2);
            r.Property(r => r.Comments).HasMaxLength(2000);
            r.Property(r => r.ReviewDate).IsRequired();
        });
    }
}