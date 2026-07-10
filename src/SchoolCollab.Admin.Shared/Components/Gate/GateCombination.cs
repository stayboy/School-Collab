namespace SchoolCollab.Admin.Shared.Components.Gate;

/// <summary>How multiple <see cref="IGateCondition"/>s combine.</summary>
public enum GateCombination
{
    /// <summary>All conditions must pass (logical AND).</summary>
    All,

    /// <summary>Any condition passing is sufficient (logical OR).</summary>
    Any
}
