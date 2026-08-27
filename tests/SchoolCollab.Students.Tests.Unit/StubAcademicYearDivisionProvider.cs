using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Test stub for <see cref="IAcademicYearDivisionProvider"/> with a fixed division
/// so period-creation tests control the framework gate (period-hierarchy FR-H7).
/// </summary>
internal sealed class StubAcademicYearDivisionProvider(string division) : IAcademicYearDivisionProvider
{
    public Task<string> GetDivisionAsync(CancellationToken ct = default) => Task.FromResult(division);
}
