using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>Test double for <see cref="ITeacherDirectory"/> (ar-20). Default:
/// <see cref="ExistsResult"/> = true so create-attribution validates unless a test flips it.
/// <see cref="ExistenceChecks"/> records every queried id for assertion.</summary>
public sealed class FakeTeacherDirectory : ITeacherDirectory
{
    public bool ExistsResult { get; set; } = true;
    public List<Guid> ExistenceChecks { get; } = [];

    public Task<bool> ExistsAsync(Guid teacherId, CancellationToken cancellationToken = default)
    {
        ExistenceChecks.Add(teacherId);
        return Task.FromResult(ExistsResult);
    }
}
