namespace SchoolCollab.Settings.Contracts.Events;

/// <summary>
/// Attribute key/value pair carried on coded-value integration events so that
/// downstream projections (e.g. the Students local coded-value read model) can
/// validate attribute-driven flows — such as enroll stream validation, which
/// reads a stream's <c>gradeLevel</c> attribute — without calling back to
/// settings-api. See documents/solution/adr-cross-module-calls.md.
/// </summary>
public record CodedValueAttributeEvent(string Key, string Value);
