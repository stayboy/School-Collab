using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>
/// Decides how a topic-edit form submit should be persisted (tcv/4, spec §D).
/// Topic edits go through the CodedValue + tenancy-override mechanism instead of
/// mutating the shared global blueprint directly:
///   • No CodedValue backs the topic        → direct name/display-order edit.
///   • Code AND description both change      → new tenant-scoped provisional
///     CodedValue (tcv/3) + repoint the topic at it (override is impossible).
///   • An override already exists, or code changes
///                                           → write the tenant override.
///   • Otherwise (no override, only name/desc) → edit the main CodedValue in place.
///
/// This is a pure function so the routing rules are unit-testable without a
/// bUnit render (the dialog's submit path cannot be exercised in bUnit — see the
/// async-submit deadlock constraint).
/// </summary>
public static class TopicEditRouter
{
    public enum Action
    {
        /// No CodedValue backing → fall back to direct name/display-order edit.
        DirectNameOnly,

        /// Write a tenant override for an existing CodedValue (UpsertOverrideAsync).
        Override,

        /// Edit the main CodedValue in place (UpdateAsync) — no override exists.
        EditInPlace,

        /// Code AND description both changed → create a provisional CodedValue and
        /// repoint the topic at it (tcv/3).
        CreateProvisional,
    }

    public sealed record Plan(
        Action Action,
        Guid? CodedValueId,
        string Name,
        string? Code,
        string? Description,
        Guid? ParentId,
        int DisplayOrder);

    public static Plan Decide(
        CodedValueDto? cv,
        string name,
        string? code,
        string? description,
        int displayOrder)
    {
        var trimmedCode = code?.Trim();
        var trimmedDescription = description?.Trim();

        if (cv is null)
        {
            return new Plan(Action.DirectNameOnly, null, name, null, null, null, displayOrder);
        }

        var codeChanged = !string.Equals(trimmedCode, cv.Code, StringComparison.OrdinalIgnoreCase);
        var descriptionChanged = !string.Equals(trimmedDescription ?? "", cv.Description ?? "", StringComparison.OrdinalIgnoreCase);

        if (codeChanged && descriptionChanged)
        {
            // Override cannot change Code AND Description together (tcv/1 guard) —
            // fall back to a new tenant-scoped provisional CodedValue (tcv/3).
            return new Plan(Action.CreateProvisional, cv.Id, name, trimmedCode, trimmedDescription, cv.ParentId, displayOrder);
        }

        // A code change (not accompanied by a description change) always goes
        // through the override mechanism (editing a code in place is unsupported).
        if (codeChanged || cv.IsOverridden)
        {
            return new Plan(
                Action.Override,
                cv.Id,
                name,
                codeChanged ? trimmedCode : null,
                descriptionChanged ? trimmedDescription : null,
                cv.ParentId,
                displayOrder);
        }

        // No override exists and only name/description changed → edit the shared
        // blueprint in place (requirement 5).
        return new Plan(Action.EditInPlace, cv.Id, name, null, trimmedDescription, cv.ParentId, displayOrder);
    }
}
