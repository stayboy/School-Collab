using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Contracts.Events;
using SchoolCollab.Students.Core.CQRS.Students.Commands.CreateStudentWithLinkedData;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.Students.Commands.UpdateStudentWithLinkedData;

/// <summary>
/// Atomically updates a student's profile and reconciles its guardians and contacts in a
/// single unit of work (the edit counterpart of <c>CreateStudentWithLinkedDataHandler</c>).
/// Everything succeeds or fails together — no profile saved with a half-applied guardian
/// set, no orphaned link, no "student updated but contacts stale" state.
///
/// Optimistic concurrency (three layers):
///   1. <c>ExpectedRowVersion</c> vs the student's current <c>xmin</c> — catches a
///      concurrent profile change since the client loaded.
///   2. Loaded-id subset checks — catches a guardian/contact added OR removed by another
///      user since the client loaded (a blind reconcile would otherwise delete a
///      concurrently-added row or resurrect a concurrently-removed one).
///   3. EF Core <c>xmin</c> on every touched row at <c>SaveChanges</c> — catches a
///      concurrent edit to a guardian-link or contact the client saw.
/// </summary>
public sealed class UpdateStudentWithLinkedDataHandler(
    IUnitOfWork<StudentsDbContext> uow,
    HybridCache cache,
    ITenantProvider tenantProvider,
    IIntegrationEventPublisher publisher,
    ILogger<UpdateStudentWithLinkedDataHandler> logger)
    : ICommandHandler<UpdateStudentWithLinkedData>
{
    public async Task HandleAsync(
        UpdateStudentWithLinkedData command,
        CancellationToken cancellationToken = default)
    {
        // FR-4: no strict entity may be written with an empty tenant.
        tenantProvider.RequireTenantContext(nameof(UpdateStudentWithLinkedData), typeof(Student));

        var studentUpdatedEvents = new List<StudentUpdated>();

        await uow.ExecuteAsync(async (ctx, ct) =>
        {
            var student = await ctx.Students
                .SingleOrDefaultAsync(s => s.Id == command.Id, ct)
                ?? throw new StudentNotFoundException(command.Id);

            // Layer 1: profile concurrency — the client must be editing the version it loaded.
            if (command.ExpectedRowVersion != student.RowVersion)
                throw new ConcurrencyException("Student", student.Id);

            // Load the current guardian links + contacts as TRACKED entities so EF Core's
            // xmin check covers every row we touch at SaveChanges.
            var currentLinks = await ctx.StudentGuardians
                .Where(sg => sg.StudentId == command.Id)
                .ToListAsync(ct);
            var currentContacts = await ctx.Contacts
                .Where(c => c.OwnerType == ContactOwnerType.Student
                            && c.OwnerId == command.Id
                            && !c.IsDeleted)
                .ToListAsync(ct);

            // Layer 2: concurrent child additions/removals since the client loaded.
            var loadedGuardianIds = command.LoadedGuardianIds ?? [];
            var loadedContactIds = command.LoadedContactIds ?? [];
            var currentGuardianIds = currentLinks.Select(l => l.GuardianId).ToHashSet();
            var currentContactIds = currentContacts.Select(c => c.Id).ToHashSet();
            if (currentGuardianIds.Except(loadedGuardianIds).Any()
                || loadedGuardianIds.Except(currentGuardianIds).Any()
                || currentContactIds.Except(loadedContactIds).Any()
                || loadedContactIds.Except(currentContactIds).Any())
            {
                throw new ConcurrencyException("Student", student.Id);
            }

            // Profile update (raises StudentUpdatedEvent).
            student.Update(
                command.FirstName,
                command.LastName,
                command.DateOfBirth,
                command.GenderCodedValueId,
                command.TitleCodedValueId);

            ReconcileGuardians(ctx, student.Id, command.Guardians ?? [], currentLinks);
            ReconcileContacts(ctx, student.Id, command.Contacts ?? [], currentContacts);

            studentUpdatedEvents.AddRange(
                student.DomainEvents.OfType<StudentUpdatedEvent>().Select(evt =>
                    new StudentUpdated(student.Id, student.StudentNumber, student.FirstName,
                        student.LastName, student.UpdatedAt)));

            try
            {
                // Layer 3: EF xmin check on every touched row.
                await ctx.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyException("Student", student.Id);
            }

            logger.LogInformation(
                "Student {Id} updated with {GuardianCount} guardian(s) and {ContactCount} contact(s)",
                student.Id, command.Guardians?.Length ?? 0, command.Contacts?.Length ?? 0);

            return true;
        }, cancellationToken);

        // After the commit: invalidate cache + enqueue outbox events (non-transactional,
        // so they must stay after the UoW returns — a rollback can never leave a phantom event).
        await cache.RemoveByTagAsync("students", cancellationToken);
        await cache.RemoveByTagAsync("guardians", cancellationToken);
        await cache.RemoveByTagAsync("contacts", cancellationToken);

        foreach (var e in studentUpdatedEvents)
            await publisher.EnqueueAsync(e, cancellationToken);
    }

    /// <summary>
    /// Reconciles the student's guardian links to the draft set: unlink links absent from
    /// the draft, update link metadata for existing links, link existing guardians that
    /// aren't yet linked, and create+link brand-new guardians. All inside the UoW.
    /// </summary>
    private void ReconcileGuardians(
        StudentsDbContext ctx,
        Guid studentId,
        IReadOnlyList<GuardianDraft> drafts,
        List<StudentGuardian> currentLinks)
    {
        var draftGuardianIds = drafts
            .Where(g => g.ExistingGuardianId is not null)
            .Select(g => g.ExistingGuardianId!.Value)
            .ToHashSet();

        // Unlink links the client dropped.
        foreach (var link in currentLinks.Where(l => !draftGuardianIds.Contains(l.GuardianId)).ToList())
        {
            ctx.StudentGuardians.Remove(link);
        }

        foreach (var draft in drafts)
        {
            if (draft.ExistingGuardianId is { } existingId)
            {
                var link = currentLinks.FirstOrDefault(l => l.GuardianId == existingId);
                if (link is null)
                {
                    // Link an existing guardian that wasn't linked before.
                    ctx.StudentGuardians.Add(
                        StudentGuardian.Create(studentId, existingId, draft.Role,
                                draft.RelationshipCodedValueId, draft.IsEmergencyContact,
                                draft.ActingGuardianId)
                            .WithTenant(tenantProvider));
                }
                else
                {
                    // Update link metadata (role / relationship / emergency).
                    link.Update(draft.Role, draft.RelationshipCodedValueId, draft.IsEmergencyContact);
                }
            }
            else
            {
                // Brand-new guardian: create it, then link it.
                var guardianId = AddNewGuardian(ctx, draft);
                // Create the new guardian's initial contacts (only for
                // newly-created guardians — existing guardians keep their
                // contacts, edited on the guardian surface).
                if (draft.Contacts is { Length: > 0 })
                {
                    foreach (var c in draft.Contacts)
                    {
                        ctx.Contacts.Add(
                            Contact.Create(ContactOwnerType.Guardian, guardianId,
                                    c.Channel, c.Value, c.Label, c.CountryCode,
                                    c.DisplayOrder)
                                .WithTenant(tenantProvider));
                    }
                }
                ctx.StudentGuardians.Add(
                    StudentGuardian.Create(studentId, guardianId, draft.Role,
                            draft.RelationshipCodedValueId, draft.IsEmergencyContact,
                            draft.ActingGuardianId)
                        .WithTenant(tenantProvider));
            }
        }
    }

    /// <summary>
    /// Reconciles the student's contacts to the draft set: soft-delete contacts absent
    /// from the draft, update contacts matched by id, add new contacts. All inside the UoW.
    /// </summary>
    private void ReconcileContacts(
        StudentsDbContext ctx,
        Guid studentId,
        IReadOnlyList<ContactDraft> drafts,
        List<Contact> currentContacts)
    {
        var draftContactIds = drafts
            .Where(c => c.Id is not null)
            .Select(c => c.Id!.Value)
            .ToHashSet();

        // Soft-delete contacts the client dropped.
        foreach (var contact in currentContacts.Where(c => !draftContactIds.Contains(c.Id)).ToList())
        {
            contact.SoftDelete();
        }

        foreach (var draft in drafts)
        {
            if (draft.Id is { } contactId)
            {
                var contact = currentContacts.FirstOrDefault(c => c.Id == contactId);
                if (contact is not null)
                {
                    contact.Update(draft.Value, draft.Label, draft.CountryCode);
                    contact.SetDisplayOrder(draft.DisplayOrder);
                }
                // A draft id not in currentContacts is a concurrent deletion — the
                // loaded-id subset check already rejected the save, so this is unreachable.
            }
            else
            {
                ctx.Contacts.Add(
                    Contact.Create(ContactOwnerType.Student, studentId, draft.Channel,
                            draft.Value, draft.Label, draft.CountryCode, draft.DisplayOrder)
                        .WithTenant(tenantProvider));
            }
        }
    }

    /// <summary>
    /// Creates a brand-new guardian (with its initial name-history snapshot) and returns
    /// its id. Mirrors <c>CreateStudentWithLinkedDataHandler.AddNewGuardian</c>.
    /// </summary>
    private Guid AddNewGuardian(StudentsDbContext ctx, GuardianDraft draft)
    {
        var guardian = Guardian.Create(
                draft.TitleCodedValueId,
                draft.FirstName!,
                draft.LastName!,
                displayName: null,
                address: null,
                communityId: null,
                draft.DateOfBirth,
                draft.GenderCodedValueId)
            .WithTenant(tenantProvider);

        guardian.AddInitialNameHistory();
        ctx.Guardians.Add(guardian);
        foreach (var h in guardian.NameHistory)
            ctx.GuardianNameHistories.Add(h);

        return guardian.Id;
    }
}
