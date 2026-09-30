using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>
/// Persists stream field edits through the CodedValue + tenancy-override
/// mechanism, mirroring <see cref="TopicCodedValueSaver"/>. The routing decision
/// itself is the shared, topic-agnostic <see cref="TopicEditRouter.Decide"/> —
/// reused, not duplicated; only the persistence of each decision differs for
/// streams:
/// <list type="bullet">
///   <item><b>No CodedValue backs the stream</b> (<c>DirectNameOnly</c>) — a stream
///   with no backing coded value cannot exist (the grade↔stream link IS a coded
///   value), so this means "create a brand-new <c>GRSTREAMS</c> child" via the
///   ORDINARY create endpoint with <c>ParentId</c> = the GRSTREAMS parent. It is
///   deliberately NOT <c>CreateProvisionalCodedValueAsync</c>: provisional values
///   belong to the topic tcv/3 approval flow and would be invisible to the bridge
///   read path, which queries <c>by-parent?parentCode=GRSTREAMS</c>.</item>
///   <item><b>An override already exists, or the code changes</b> (<c>Override</c>) —
///   write the tenant override (<c>UpsertOverrideAsync</c>).</item>
///   <item><b>Otherwise</b> (<c>EditInPlace</c>) — edit the coded value in place
///   (<c>UpdateAsync</c>): a tenant-owned value is the row itself, so there is
///   nothing to override.</item>
///   <item><b>Code AND description both change</b> (<c>CreateProvisional</c>) — an
///   override cannot carry both, so the same create-new route as above is used: a
///   new <c>GRSTREAMS</c> child (never a provisional value).</item>
/// </list>
/// </summary>
public static class StreamCodedValueSaver
{
    /// <summary>
    /// Saves the edited stream fields and returns the resolved <c>CodedValueId</c>
    /// (possibly a newly created <c>GRSTREAMS</c> child — or null when nothing could
    /// be persisted) plus the effective code. When nothing changed, no write is issued.
    /// </summary>
    /// <param name="streamsParentId">
    /// The <c>GRSTREAMS</c> parent coded value id — the parent a brand-new stream is
    /// created under.
    /// </param>
    public static async Task<(Guid? CodedValueId, string? EffectiveCode)> SaveAsync(
        CodedValuesApiClient codedValues,
        CodedValueDto? cv,
        Guid streamsParentId,
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
            case TopicEditRouter.Action.Override:
                await codedValues.UpsertOverrideAsync(
                    plan.CodedValueId!.Value, plan.Name, plan.Description, plan.Code);
                return (plan.CodedValueId, plan.Code ?? cv?.Code);

            case TopicEditRouter.Action.EditInPlace:
                await codedValues.UpdateAsync(
                    plan.CodedValueId!.Value,
                    new UpdateCodedValueRequest(plan.Name, plan.Description, plan.DisplayOrder));
                return (plan.CodedValueId, code?.Trim());

            case TopicEditRouter.Action.DirectNameOnly:
            case TopicEditRouter.Action.CreateProvisional:
            default:
                var newCode = (plan.Code ?? code)?.Trim();
                if (string.IsNullOrWhiteSpace(newCode))
                {
                    throw new InvalidOperationException(
                        "A stream needs a code — enter one to create a new stream.");
                }

                var newId = await codedValues.CreateAsync(new CreateCodedValueRequest(
                    newCode, plan.Name, plan.Description, streamsParentId, plan.DisplayOrder));
                return (newId, newCode);
        }
    }
}
