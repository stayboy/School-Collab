namespace SchoolCollab.Settings.Core.Domain.Exceptions;

/// <summary>Thrown when no active <see cref="EntityCodeRule"/> exists for a requested code,
/// or when a rule is not found by id.</summary>
public class EntityCodeRuleNotFoundException : DomainException
{
    public string? RuleCode { get; }
    public Guid? RuleId { get; }

    public EntityCodeRuleNotFoundException(string ruleCode)
        : base($"No active entity code generation rule found for code '{ruleCode}'.")
    {
        RuleCode = ruleCode;
    }

    public EntityCodeRuleNotFoundException(Guid ruleId)
        : base($"Entity code generation rule '{ruleId}' was not found.")
    {
        RuleId = ruleId;
    }
}