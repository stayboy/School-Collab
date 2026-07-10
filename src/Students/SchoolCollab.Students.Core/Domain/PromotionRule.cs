namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Decides the grade level a student moves to when a period closes and they are
/// carried into the next period. The default rule promotes one level if a higher
/// grade level exists for the tenant, otherwise repeats at the same level (FR-A4).
/// </summary>
public interface IPromotionRule
{
    /// <summary>
    /// Returns the grade level id the student should move to in the next period.
    /// </summary>
    /// <param name="fromGradeLevel">The student's current grade level.</param>
    /// <param name="tenantGradeLevels">All grade levels for the tenant.</param>
    Guid Resolve(GradeLevel fromGradeLevel, IReadOnlyCollection<GradeLevel> tenantGradeLevels);
}

public sealed class DefaultPromotionRule : IPromotionRule
{
    public Guid Resolve(GradeLevel fromGradeLevel, IReadOnlyCollection<GradeLevel> tenantGradeLevels)
    {
        foreach (var g in tenantGradeLevels)
        {
            if (g.Level == fromGradeLevel.Level + 1)
            {
                return g.Id;
            }
        }

        // No higher grade level exists → repeat at the same grade level.
        return fromGradeLevel.Id;
    }
}
