using System.Text.Json.Serialization;

namespace SchoolCollab.Core.AssignmentPolicies;

/// <summary>
/// Guardian-signature requirement for an assignment (owner decision D2,
/// <c>documents/solution/assignment-policy-fields.md</c>). Three states rather than a
/// boolean, because "no signature" and "signature pre-filled but changeable" are
/// different author experiences:
/// <list type="bullet">
///   <item><see cref="Disabled"/> — a guardian signature is never required (the old
///         <c>RequiresSignatureDefault = false</c>).</item>
///   <item><see cref="Optional"/> — the create wizard pre-fills the signature checkbox
///         from the policy; the author may still change it (the old
///         <c>RequiresSignatureDefault = true</c>).</item>
///   <item><see cref="Mandatory"/> — the author cannot turn the signature off
///         (Round B locks the checkbox with an explanatory tooltip, mirroring the
///         AI-prompt <c>PromptLocked</c> precedent).</item>
/// </list>
/// Stored as text in both policy tables (<c>HasConversion&lt;string&gt;()</c>), and travels on the
/// wire as its <b>name</b>: the type-level converter keeps the JSON shape identical for every
/// host and every client without a per-host <c>JsonStringEnumConverter</c> registration — the
/// drift class <c>ProgramJsonEnumConverterTests</c> exists to catch (the
/// <c>GeneratedQuestionType</c> precedent). Reading is tolerant: the converter still accepts the
/// numeric form.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SignatureRequirementMode>))]
public enum SignatureRequirementMode
{
    /// <summary>No guardian signature is required.</summary>
    Disabled = 0,

    /// <summary>A signature is pre-filled as the default, but the author may change it.</summary>
    Optional = 1,

    /// <summary>A signature is required and the author cannot disable it.</summary>
    Mandatory = 2,
}
