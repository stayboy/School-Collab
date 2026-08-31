namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// The value type a <see cref="FeatureFlag"/> carries. Only <see cref="Boolean"/>
/// remains — the value-valued <c>String</c> kind was removed in Rev. 2 when the
/// academic-year division moved onto the Students <c>Period</c> entity
/// (period-hierarchy-terms-semesters.md §8.2).
/// </summary>
public enum FlagKind
{
    Boolean = 0,
}