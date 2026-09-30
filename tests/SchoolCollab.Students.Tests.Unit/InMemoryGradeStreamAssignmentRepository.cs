using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Hand-rolled <see cref="IGradeStreamAssignmentRepository"/> over a test's
/// in-memory <see cref="StudentsDbContext"/> — the sanctioned shape for the
/// bridge-handler suites (the plan pins it so the cross-tenant assertions cannot
/// pass vacuously against a mocked repository).
/// </summary>
/// <remarks>
/// The in-memory provider enforces neither unique indexes nor Postgres
/// SQLSTATEs, so <see cref="AddOrReuseAsync"/> emulates the real
/// <c>ix_grade_stream_assignments_tenant_grade_stream</c> contract: the same
/// (grade, stream) pair returns the winning row instead of inserting a second
/// one. Every read still goes through the real <see cref="StudentsDbContext"/>, so
/// the EF named "Tenant" query filter (the isolation guarantee asserted by the
/// AC10 tests) is fully exercised. The real index itself is pinned separately by
/// <c>GradeStreamAssignmentTests.Model_DeclaresUniqueIndexOnTenantGradeStream</c>.
/// </remarks>
internal sealed class InMemoryGradeStreamAssignmentRepository(StudentsDbContext db) : IGradeStreamAssignmentRepository
{
    public Task<GradeStreamAssignment?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.GradeStreamAssignments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<GradeStreamAssignment> AddOrReuseAsync(
        GradeStreamAssignment candidate, CancellationToken cancellationToken = default)
    {
        var winner = await db.GradeStreamAssignments.FirstOrDefaultAsync(
            x => x.GradeLevelId == candidate.GradeLevelId
              && x.StreamCodedValueId == candidate.StreamCodedValueId, cancellationToken);
        if (winner is not null) return winner;

        db.GradeStreamAssignments.Add(candidate);
        await db.SaveChangesAsync(cancellationToken);
        return candidate;
    }

    public async Task<GradeStreamAssignment[]> ListByGradeLevelAsync(
        Guid gradeLevelId, CancellationToken cancellationToken = default) =>
        await db.GradeStreamAssignments
            .AsNoTracking()
            .Where(x => x.GradeLevelId == gradeLevelId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public async Task<GradeStreamAssignment[]> ListByGradeLevelForUpdateAsync(
        Guid gradeLevelId, CancellationToken cancellationToken = default) =>
        await db.GradeStreamAssignments
            .AsTracking()
            .Where(x => x.GradeLevelId == gradeLevelId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public async Task<int> GetNextDisplayOrderAsync(
        Guid gradeLevelId, CancellationToken cancellationToken = default)
    {
        var orders = await db.GradeStreamAssignments
            .Where(x => x.GradeLevelId == gradeLevelId)
            .Select(x => x.DisplayOrder)
            .ToArrayAsync(cancellationToken);
        return orders.Length == 0 ? 0 : orders.Max() + 1;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public Task<bool> ExistsAsync(
        Guid gradeLevelId, Guid streamCodedValueId, CancellationToken cancellationToken = default) =>
        db.GradeStreamAssignments
            .AnyAsync(x => x.GradeLevelId == gradeLevelId
                        && x.StreamCodedValueId == streamCodedValueId, cancellationToken);

    public async Task DeleteAsync(
        GradeStreamAssignment assignment, CancellationToken cancellationToken = default)
    {
        db.GradeStreamAssignments.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
    }
}
