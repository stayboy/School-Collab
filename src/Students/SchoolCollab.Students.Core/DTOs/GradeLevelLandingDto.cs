namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// Landing-page projection of a <see cref="Domain.GradeLevel"/> with counts scoped
/// to the <b>current period</b> (the period whose <c>[StartDate, EndDate]</c>
/// contains today). The current period is derived server-side (see
/// <c>ListGradeLevelsForLandingHandler</c>) — the UI never picks it.
/// </summary>
/// <remarks>
/// <para><see cref="Name"/> is the <b>mirrored</b> name copied from the coded value
/// at create/update time; it goes stale on tenant override. The landing page
/// overlays the tenant-resolved display name client-side (spec §5.4) by joining on
/// <see cref="CodedValueId"/> with the tenant-resolved GRADE coded values. The
/// mirrored name is kept here for sort/fallback only.</para>
/// <para><see cref="TopicCount"/> is <b>global</b> (topics are the shared
/// curriculum blueprint); <see cref="StudentCount"/> is <b>tenant-scoped</b> via
/// <c>Student.TenantId</c>. When there is no current period, both counts are
/// <c>0</c> and <see cref="CurrentPeriodId"/>/<see cref="CurrentPeriodName"/> are
/// <see langword="null"/>.</para>
/// <para><see cref="MinAge"/>, <see cref="MaxAge"/>, and
/// <see cref="AllowedGenderCodedValueId"/> mirror the enrollment-validation guard
/// clauses on <see cref="Domain.GradeLevel"/> (plan §2 / §9). All three are
/// nullable — <c>null</c> means "no restriction" so the landing row can render
/// the dash placeholder rather than a fake default age/gender. The landing page
/// surfaces them as two compact columns ("Age range" and "Gender") so the rules
/// are visible without opening the edit form.</para>
/// </remarks>
public sealed record GradeLevelLandingDto(
    Guid Id,
    Guid CodedValueId,
    string Name,
    int TopicCount,
    int StudentCount,
    Guid? CurrentPeriodId,
    string? CurrentPeriodName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? MinAge = null,
    int? MaxAge = null,
    Guid? AllowedGenderCodedValueId = null);
