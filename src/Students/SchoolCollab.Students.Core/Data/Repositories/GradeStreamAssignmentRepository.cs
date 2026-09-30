using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class GradeStreamAssignmentRepository(StudentsDbContext db)
    : RepositoryBase<GradeStreamAssignment, StudentsDbContext>(db), IGradeStreamAssignmentRepository
{
    public async Task<GradeStreamAssignment[]> ListByGradeLevelAsync(
        Guid gradeLevelId, CancellationToken cancellationToken = default) =>
        await Db.GradeStreamAssignments
            .AsNoTracking()
            .Where(x => x.GradeLevelId == gradeLevelId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public async Task<GradeStreamAssignment[]> ListByGradeLevelForUpdateAsync(
        Guid gradeLevelId, CancellationToken cancellationToken = default) =>
        await Db.GradeStreamAssignments
            .Where(x => x.GradeLevelId == gradeLevelId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);

    public async Task<int> GetNextDisplayOrderAsync(
        Guid gradeLevelId, CancellationToken cancellationToken = default)
    {
        // Grade rows carry a non-null order (column default 0), so the cast to
        // int? is only to make MaxAsync total on an empty grade.
        var highest = await Db.GradeStreamAssignments
            .Where(x => x.GradeLevelId == gradeLevelId)
            .Select(x => (int?)x.DisplayOrder)
            .MaxAsync(cancellationToken);
        return (highest ?? -1) + 1;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        Db.SaveChangesAsync(cancellationToken);

    public Task<bool> ExistsAsync(
        Guid gradeLevelId, Guid streamCodedValueId, CancellationToken cancellationToken = default) =>
        Db.GradeStreamAssignments
            .AnyAsync(x => x.GradeLevelId == gradeLevelId
                        && x.StreamCodedValueId == streamCodedValueId, cancellationToken);

    public async Task<GradeStreamAssignment> AddOrReuseAsync(
        GradeStreamAssignment candidate, CancellationToken cancellationToken = default)
    {
        try
        {
            await AddAsync(candidate, cancellationToken);
            return candidate;
        }
        catch (DbUpdateException ex) when (IsUniqueConflict(ex))
        {
            // A concurrent assign took the (tenant, grade, stream) slot first — reuse
            // the winner's row. The losing insert is STILL TRACKED as Added
            // (SaveChanges failed but the change tracker keeps the entity), so it must
            // be evicted here or the next SaveChanges in this command re-submits it.
            Db.Entry(candidate).State = EntityState.Detached;

            return await Db.GradeStreamAssignments
                .FirstOrDefaultAsync(x => x.GradeLevelId == candidate.GradeLevelId
                                       && x.StreamCodedValueId == candidate.StreamCodedValueId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Unique-constraint conflict on grade_stream_assignments (tenant, grade_level_id, " +
                    $"stream_coded_value_id) for grade {candidate.GradeLevelId} / stream " +
                    $"{candidate.StreamCodedValueId}, but no winning row was found afterwards.", ex);
        }
    }

    /// <summary>True when the exception is the Postgres unique violation raised by
    /// <c>ix_grade_stream_assignments_tenant_grade_stream</c> (SQLSTATE 23505).
    /// Scoped to that index so unrelated constraint failures still surface.</summary>
    private static bool IsUniqueConflict(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } pg &&
        pg.ConstraintName?.Contains("grade_stream_assignments", StringComparison.OrdinalIgnoreCase) == true;
}
