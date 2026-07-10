namespace SchoolCollab.Admin.Shared.Components.Gate;

/// <summary>How a <see cref="GateBase"/> behaves when its condition(s) are not met.</summary>
public enum GateMode
{
    /// <summary>Render <see cref="GateBase.ChildContent"/> only when the gate passes; show <see cref="GateBase.Fallback"/> (or a default banner) otherwise.</summary>
    Hide,

    /// <summary>Always render <see cref="GateBase.ChildContent"/>, but disable it (via a disabled &lt;fieldset&gt;) when the gate does not pass.</summary>
    Disable
}
