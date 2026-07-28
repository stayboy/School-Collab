namespace SchoolCollab.Settings.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a segment sequence hits its <c>UpperLimit</c> and cannot roll over
/// any further (spec §3.2 — <see cref="SegmentType.NumericSequence"/> and
/// <see cref="SegmentType.AlphabeticSequence"/> throw at the limit; only
/// <see cref="SegmentType.AlphanumericSequence"/> rolls over, and it throws once
/// the alphabetic prefix itself exceeds its max).
/// </summary>
public class EntityCodeGenerationCollisionException : DomainException
{
    public EntityCodeGenerationCollisionException(string message) : base(message) { }
}