using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.ListGradeLevelsForLanding;

/// <summary>
/// Lists every grade level with per-current-period counts for the landing page.
/// <b>No <c>PeriodId</c> parameter</b>: the handler derives the current period
/// server-side from today's date so the UI can't get out of sync. See spec §5.3.
/// </summary>
public sealed record ListGradeLevelsForLanding : IQuery<GradeLevelLandingDto[]>;