namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>
/// The single spelling of an enrollment-exception <b>count</b> across the surfaces
/// that show one (subject-period-exception-model.md v3 §5.2): the Topics landing's
/// column badge, the grade-detail card's meta line, the View-all dialog's row badge,
/// and the management page's own list badge.
///
/// <para>Introduced because the count text had been inlined at three call sites when
/// v1's period-vocabulary label type was deleted; four copies of the same pluralisation
/// is where they start to disagree. The string itself is load-bearing —
/// <c>SubjectsLandingPageTests</c> asserts the exact <c>"2 exceptions"</c> — so this is
/// the only place it is produced.</para>
/// </summary>
public static class EnrollmentExceptionLabels
{
    /// <summary>The count badge's whole content — a count, never a period list.</summary>
    public static string FormatCount(int count) => count == 1 ? "1 exception" : $"{count} exceptions";
}
