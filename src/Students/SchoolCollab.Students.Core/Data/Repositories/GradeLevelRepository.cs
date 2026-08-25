using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

internal sealed class GradeLevelRepository(StudentsDbContext db)
    : RepositoryBase<GradeLevel, StudentsDbContext>(db), IGradeLevelRepository
{
    public override async Task UpdateAsync(GradeLevel gradeLevel, CancellationToken cancellationToken = default)
    {
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(gradeLevel.Id);
        }
    }

    public async Task<GradeLevelDto[]> ListAsync(CancellationToken cancellationToken = default) =>
        await Db.GradeLevels
            .AsNoTracking()
            .OrderBy(x => x.Level)
            .Select(x => new GradeLevelDto(
                x.Id, x.CodedValueId, x.Level, x.Name, x.DisplayOrder,
                0, 0,
                x.CreatedAt, x.UpdatedAt,
                x.MinAge, x.MaxAge, x.AllowedGenderCodedValueId, x.IsBlockedFromEnrollment))
            .ToArrayAsync(cancellationToken);

    public Task<GradeLevel?> GetByCodedValueIdAsync(Guid codedValueId, CancellationToken cancellationToken = default)
        => Db.GradeLevels
            .FirstOrDefaultAsync(x => x.CodedValueId == codedValueId, cancellationToken);

    public async Task<GradeLevel> AddOrReuseAsync(GradeLevel candidate, CancellationToken cancellationToken = default)
    {
        try
        {
            await AddAsync(candidate, cancellationToken);
            return candidate;
        }
        catch (DbUpdateException ex) when (IsCodedValueUniqueConflict(ex))
        {
            // A concurrent enroll materialized this coded value first — reuse the
            // winner's row. IMPORTANT: the losing insert is STILL TRACKED as Added
            // (SaveChanges failed but the change tracker keeps the entity), so it
            // must be evicted here or the NEXT SaveChanges in this command (the
            // enrollment insert) re-submits it and fails on the same constraint.
            Db.Entry(candidate).State = EntityState.Detached;

            return await GetByCodedValueIdAsync(candidate.CodedValueId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Unique-constraint conflict on grade_levels (tenant, coded_value_id) for coded value " +
                    $"{candidate.CodedValueId}, but no winning row was found afterwards.", ex);
        }
    }

    /// <summary>True when the exception is the Postgres unique violation raised by
    /// <c>ix_grade_levels_tenant_coded_value_id</c> (SQLSTATE 23505). Scoped to the
    /// coded-value index so unrelated constraint failures still surface.</summary>
    private static bool IsCodedValueUniqueConflict(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" } pg &&
        pg.ConstraintName?.Contains("coded_value", StringComparison.OrdinalIgnoreCase) == true;
}
