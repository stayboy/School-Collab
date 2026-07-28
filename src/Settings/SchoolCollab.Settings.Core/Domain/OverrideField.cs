namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// The single field on an <see cref="EntityCodeSegment"/> that a tenant can
/// override per segment (spec §4.12). Stored as an integer column so a typo
/// in a free-form string cannot corrupt the override table; the integer
/// values must match the position of the field in the entity record (kept
/// stable by an explicit constant).
/// </summary>
/// <remarks>
/// Role, Index, and Type are NOT overridable — reordering segments or
/// changing their type would change the meaning of the rule, not its
/// format. Tenants who need a fundamentally different template create
/// their own rule with the same Code (the activate handler enforces
/// only-one-active-per-Code).
/// </remarks>
public enum OverrideField
{
    FixedText   = 0,
    Prefix      = 1,
    Suffix      = 2,
    ResetPeriod = 3,
    MinWidth    = 4,
    UpperLimit  = 5,
}
