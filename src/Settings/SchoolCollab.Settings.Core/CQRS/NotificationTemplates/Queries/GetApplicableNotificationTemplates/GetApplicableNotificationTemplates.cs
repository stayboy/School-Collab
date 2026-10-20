using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetApplicableNotificationTemplates;

/// <summary>
/// Which of the tenant's templates <b>can apply</b> to one grade (spec §3.11), grouped into the eight
/// authorable kind × channel slots for the grade-detail card.
/// <para>
/// Applicability here is deliberately narrower than the publish-time matcher (spec §3.1.1): only the
/// <c>GradeLevel</c> dimension filters — absent, or holding this grade among its values. Every other
/// constrained dimension is <b>displayed</b>, never filtered: a template constrained to another stream
/// still "can apply" to this grade, because the card is an inventory of what may fire, not an
/// evaluation of what will.
/// </para>
/// </summary>
public sealed record GetApplicableNotificationTemplates(Guid GradeLevelId)
    : IQuery<ApplicableNotificationTemplatesDto>;
