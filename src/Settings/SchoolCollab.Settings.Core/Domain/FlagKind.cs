namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// The value type a <see cref="FeatureFlag"/> carries. Only <see cref="Boolean"/>
/// is supported in v1; the column shape is forward-compatible for future kinds.
/// </summary>
public enum FlagKind
{
    Boolean = 0,
}