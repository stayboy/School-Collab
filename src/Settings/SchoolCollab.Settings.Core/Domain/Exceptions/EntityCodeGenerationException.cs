namespace SchoolCollab.Settings.Core.Domain.Exceptions;

/// <summary>
/// Thrown when an entity code cannot be generated (e.g. no active rule for the
/// requested code, or the rule has no segments).
/// </summary>
public class EntityCodeGenerationException : DomainException
{
    public string RuleCode { get; }

    public EntityCodeGenerationException(string ruleCode, string message)
        : base($"Entity code generation failed for rule '{ruleCode}': {message}")
    {
        RuleCode = ruleCode;
    }
}