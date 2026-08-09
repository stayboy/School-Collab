using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>
/// Persists topic field edits through the CodedValue + tenancy-override
/// mechanism (tcv/4, spec §D). Shared by TopicCreateDialog and TopicEditDialog
/// so both flows save edits identically:
///   • No CodedValue backs the topic        → direct name/display-order edit.
///   • Code AND description both change      → new tenant-scoped provisional
///     CodedValue (tcv/3) + repoint the topic at it (override is impossible).
///   • An override already exists, or code changes
///                                           → write the tenant override.
///   • Otherwise (no override, only name/desc) → edit the main CodedValue in place.
///
/// The one exception is code generation from the template rule (the create
/// dialog's "regenerate template code" button) — that is a pure preview/regenerate
/// action, not a persisted edit, so it is handled by the dialog, not here.
/// </summary>
public static class TopicCodedValueSaver
{
    /// <summary>
    /// Saves the edited topic fields against the given coded value and returns
    /// the resolved <c>CodedValueId</c> (possibly repointed to a new provisional
    /// value) and the effective code. When nothing changed, no write is issued.
    /// </summary>
    public static async Task<(Guid? CodedValueId, string? EffectiveCode)> SaveAsync(
        CodedValuesApiClient codedValues,
        CodedValueDto? cv,
        string name,
        string? code,
        string? description,
        int displayOrder)
    {
        // Nothing changed → return the coded value as-is (no write).
        if (cv is not null
            && string.Equals(name.Trim(), cv.Name, StringComparison.Ordinal)
            && string.Equals(code?.Trim(), cv.Code, StringComparison.OrdinalIgnoreCase)
            && string.Equals(description?.Trim() ?? "", cv.Description ?? "", StringComparison.Ordinal)
            && displayOrder == cv.DisplayOrder)
        {
            return (cv.Id, cv.Code);
        }

        var plan = TopicEditRouter.Decide(cv, name, code, description, displayOrder);

        switch (plan.Action)
        {
            case TopicEditRouter.Action.DirectNameOnly:
                return (null, code?.Trim());

            case TopicEditRouter.Action.Override:
                await codedValues.UpsertOverrideAsync(
                    plan.CodedValueId!.Value, plan.Name, plan.Description, plan.Code);
                return (plan.CodedValueId, plan.Code ?? cv?.Code);

            case TopicEditRouter.Action.EditInPlace:
                await codedValues.UpdateAsync(
                    plan.CodedValueId!.Value,
                    new UpdateCodedValueRequest(plan.Name, plan.Description, plan.DisplayOrder));
                return (plan.CodedValueId, code?.Trim());

            case TopicEditRouter.Action.CreateProvisional:
                var newId = await codedValues.CreateProvisionalCodedValueAsync(
                    new CreateProvisionalCodedValueRequest(
                        plan.Code ?? string.Empty,
                        plan.Name,
                        plan.Description,
                        plan.ParentId,
                        plan.DisplayOrder));
                return (newId, plan.Code);

            default:
                return (null, code?.Trim());
        }
    }
}
