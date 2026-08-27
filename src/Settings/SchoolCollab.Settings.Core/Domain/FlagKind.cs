namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// The value type a <see cref="FeatureFlag"/> carries. <see cref="Boolean"/>
/// is the v1 kind; <see cref="String"/> is added for value-valued tenant
/// settings (e.g. <c>academic_year_division</c>) — period-hierarchy
/// period-hierarchy-terms-semesters.md FR-H6.
/// </summary>
public enum FlagKind
{
    Boolean = 0,
    String = 1,
}