namespace SchoolCollab.Admin.Shared.Components.Gate;

/// <summary>A single async boolean gate condition (e.g. tenant selected, feature enabled).</summary>
public interface IGateCondition
{
    /// <summary>Evaluate the condition. Awaiting this yields the gate's pass/fail for this condition.</summary>
    Task<bool> EvaluateAsync(CancellationToken ct = default);
}
