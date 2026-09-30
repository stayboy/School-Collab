using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.GradeStreams.Commands.SetGradeStreamOrder;

/// <summary>
/// Moves one of a grade's streams to the requested position in that grade's list
/// (the bridge row's <c>DisplayOrder</c>). The grade's rows are normalised to
/// unique contiguous orders first (the migration backfill preserves the old
/// listing order and the seeder batch-inserts, so equal <c>CreatedAt</c> values —
/// and a hand-edited order — must not derail the swap), then the mover and the row
/// currently at the requested position trade places: every other row keeps its
/// order.
/// </summary>
/// <remarks>
/// Mirrors <c>SetContactOrderHandler</c> for the failure surface: a row-version
/// conflict surfaces as <see cref="ConcurrencyException"/> (409 at the endpoint),
/// and the <c>students</c> cache tag is evicted because the cached curriculum
/// payload carries the order (sibling parity with the assign/remove stream
/// handlers).
/// </remarks>
public sealed class SetGradeStreamOrderHandler(
    IGradeStreamAssignmentRepository repository,
    HybridCache cache,
    ILogger<SetGradeStreamOrderHandler> logger) : ICommandHandler<SetGradeStreamOrder>
{
    public async Task HandleAsync(
        SetGradeStreamOrder command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Handling SetGradeStreamOrder grade {GradeLevelId} assignment {AssignmentId} to position {Order}",
            command.GradeLevelId, command.AssignmentId, command.Order);

        // Tracked load — the normalisation and the swap mutate several rows in one save.
        var rows = await repository.ListByGradeLevelForUpdateAsync(command.GradeLevelId, cancellationToken);

        // The query is already scoped to the grade, so "unknown assignment" and
        // "assignment belongs to another grade" collapse into the same 404.
        var mover = rows.FirstOrDefault(x => x.Id == command.AssignmentId)
            ?? throw new InvalidOperationException(
                $"Grade stream assignment '{command.AssignmentId}' does not belong to grade level '{command.GradeLevelId}'.");

        // Normalise: sort by (DisplayOrder, CreatedAt, Id) and renumber 0..n-1. The
        // tiebreak matches the migration backfill's, so a seeded grade is ordered
        // exactly as it rendered before the column existed.
        var ordered = rows
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            ordered[i].SetDisplayOrder(i);
        }

        if (command.Order < 0 || command.Order >= ordered.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command.Order), command.Order,
                $"Order must be within [0, {ordered.Length - 1}] for grade level '{command.GradeLevelId}'.");
        }

        // Swap: the mover takes the requested position, the row that held it takes
        // the mover's old position. No other row moves.
        var moverIndex = Array.IndexOf(ordered, mover);
        var displaced = ordered[command.Order];
        mover.SetDisplayOrder(command.Order);
        displaced.SetDisplayOrder(moverIndex);

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("GradeStreamAssignment", mover.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);
        logger.LogInformation(
            "Stream assignment {AssignmentId} moved to position {Order} for grade {GradeLevelId}",
            mover.Id, command.Order, command.GradeLevelId);
    }
}
