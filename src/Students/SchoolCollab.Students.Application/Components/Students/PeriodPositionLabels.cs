using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>
/// The single spelling of a sub-period's 1-based <b>position</b> — <c>1st</c>, <c>2nd</c>, …
/// (subject-period-exception-model.md v5 §0 decision 15) — across the surfaces that name one:
/// the period form's Sequence row (<c>2nd term</c>) and the enrollment-exceptions page's
/// position choice (<c>2nd</c>). Introduced for the same reason as
/// <see cref="EnrollmentExceptionLabels"/>: two copies of "which suffix does 21 get" is where
/// they start to disagree.
/// </summary>
public static class PeriodPositionLabels
{
    /// <summary>The bare position: <c>1st</c>, <c>2nd</c>, <c>3rd</c>, <c>21st</c>.</summary>
    public static string Number(int position) => $"{position}{Suffix(position)}";

    /// <summary>The position AND the kind of run it sits in: <c>2nd term</c> / <c>2nd semester</c>.
    /// Only a sub-period carries a position — the entity refuses one on a top-level academic year —
    /// so a <see cref="AcademicYearDivision.None"/> division reads <c>term</c> here.</summary>
    public static string NumberAndKind(int position, AcademicYearDivision division) =>
        $"{Number(position)} {(division == AcademicYearDivision.Semesters ? "semester" : "term")}";

    /// <summary>"1st" / "2nd" / "3rd" / "4th", with 11th–13th handled so a backfilled higher
    /// position cannot render as "21th".</summary>
    private static string Suffix(int position)
    {
        var suffix = position % 100 is >= 11 and <= 13
            ? "th"
            : (position % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };
        return suffix;
    }
}
