using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

[TestClass]
public class AcademicYearSuggestionTests
{
    private static PeriodDto MakePeriod(DateOnly start, DateOnly end) =>
        new(Guid.NewGuid(), "p", start, end, "Completed", null, null, "None", default, default);

    [TestMethod]
    public void ForDate_InSeptember_ReturnsCurrentYearWindow()
    {
        var result = AcademicYearSuggestion.ForDate(new DateOnly(2026, 9, 15));

        result.Name.Should().Be("2026/2027");
        result.StartDate.Should().Be(new DateOnly(2026, 9, 1));
        result.EndDate.Should().Be(new DateOnly(2027, 8, 31));
    }

    [TestMethod]
    public void ForDate_InJanuary_ReturnsPreviousYearWindow()
    {
        var result = AcademicYearSuggestion.ForDate(new DateOnly(2026, 1, 15));

        result.Name.Should().Be("2025/2026");
        result.StartDate.Should().Be(new DateOnly(2025, 9, 1));
        result.EndDate.Should().Be(new DateOnly(2026, 8, 31));
    }

    [TestMethod]
    public void ForDate_OnAugustThirtyFirst_BelongsToPreviousYearWindow()
    {
        var result = AcademicYearSuggestion.ForDate(new DateOnly(2026, 8, 31));

        result.Name.Should().Be("2025/2026");
        result.StartDate.Should().Be(new DateOnly(2025, 9, 1));
    }

    [TestMethod]
    public void ForDate_OnSeptemberFirst_BelongsToCurrentYearWindow()
    {
        var result = AcademicYearSuggestion.ForDate(new DateOnly(2026, 9, 1));

        result.Name.Should().Be("2026/2027");
        result.StartDate.Should().Be(new DateOnly(2026, 9, 1));
    }

    [TestMethod]
    public void BackfillFrom_NoPeriods_FallsBackToToday()
    {
        var result = AcademicYearSuggestion.BackfillFrom([]);

        var expected = AcademicYearSuggestion.ForDate(DateOnly.FromDateTime(DateTime.Today));
        result.Should().Be(expected);
    }

    [TestMethod]
    public void BackfillFrom_PicksLatestPeriodAndAdvancesOneAcademicYear()
    {
        var periods = new[]
        {
            MakePeriod(new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31)),
            MakePeriod(new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31)),
        };

        var result = AcademicYearSuggestion.BackfillFrom(periods);

        result.Name.Should().Be("2026/2027");
        result.StartDate.Should().Be(new DateOnly(2026, 9, 1));
        result.EndDate.Should().Be(new DateOnly(2027, 8, 31));
    }
}
