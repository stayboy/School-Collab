using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.GetGradeLevelByCodedValue;

/// <summary>
/// Returns the grade level for a given coded-value id, or null. Backs the
/// <c>GET /grade-levels/by-coded-value/{codedValueId}</c> read used by the wizard's
/// find-or-create flow (§6.3 "GetByCodedValueIdAsync + create fallback").
/// </summary>
public sealed record GetGradeLevelByCodedValue(Guid CodedValueId) : IQuery<GradeLevelDto?>;