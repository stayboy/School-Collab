namespace SchoolCollab.Students.Admin.Services;

/// <summary>
/// Computes suggested academic-year period boundaries used to prefill the period
/// create form. The convention is a school year running 1 September &ndash; 31 August
/// (the common academic-year window). Change <see cref="StartMonth"/> /
/// <see cref="StartDay"/> to match a different calendar.
///
/// The values returned are <b>suggestions only</b> &mdash; the user reviews and edits
/// them before saving (see the Periods "Open a term" / create form).
/// </summary>
public static class AcademicYearSuggestion
{
    /// <summary>First month of the academic year (1 = January). Default: 9 (September).</summary>
    public const int StartMonth = 9;

    /// <summary>First day of the academic year. Default: 1.</summary>
    public const int StartDay = 1;

    public static DateOnly AcademicYearStart(int startYear) => new(startYear, StartMonth, StartDay);

    public static DateOnly AcademicYearEnd(int startYear) => new(startYear + 1, 8, 31); // 31 August

    /// <summary>
    /// The academic year that contains <paramref name="reference"/>, as a
    /// (Name, StartDate, EndDate) tuple. <see cref="Name"/> is "YYYY/YYYY+1".
    /// </summary>
    public static (string Name, DateOnly StartDate, DateOnly EndDate) ForDate(DateOnly reference)
    {
        var startYear = reference.Month >= StartMonth ? reference.Year : reference.Year - 1;
        return ($"{startYear}/{startYear + 1}", AcademicYearStart(startYear), AcademicYearEnd(startYear));
    }

    /// <summary>
    /// Suggests the next academic year after the most recent existing period, used
    /// by the "Backfill" action. When there are no periods yet, falls back to the
    /// current date.
    /// </summary>
    public static (string Name, DateOnly StartDate, DateOnly EndDate) BackfillFrom(IEnumerable<PeriodDto>? periods)
    {
        var latest = periods?.OrderByDescending(p => p.EndDate).FirstOrDefault();
        var reference = latest is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : latest.EndDate.AddDays(1);
        return ForDate(reference);
    }
}
