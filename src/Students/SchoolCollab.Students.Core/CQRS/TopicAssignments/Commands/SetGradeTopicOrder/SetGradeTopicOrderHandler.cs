using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.SetGradeTopicOrder;

/// <summary>
/// Moves one of a grade's subjects to the requested position in that grade's
/// curriculum list (the bridge row's <c>DisplayOrder</c>). The grade's effective
/// rows are normalised to unique contiguous orders first, then the mover and the
/// assignment currently at the requested position trade places: every other row
/// keeps its order.
/// </summary>
/// <remarks>
/// <para>The command carries no grade id, so the grade scope is derived from the
/// loaded, tenant-filtered bridge row. An unknown, other-tenant, or
/// activity-group assignment id — and an assignment outside its effective window,
/// which the grid does not show — all map to 404 at the endpoint.</para>
/// <para>Mirrors <c>SetContactOrderHandler</c> for the failure surface: a
/// row-version conflict surfaces as <see cref="ConcurrencyException"/> (409 at the
/// endpoint), and the <c>students</c> cache tag is evicted because the cached
/// curriculum payload carries the order.</para>
/// </remarks>
public sealed class SetGradeTopicOrderHandler(
    IGradeTopicAssignmentRepository repository,
    HybridCache cache,
    ILogger<SetGradeTopicOrderHandler> logger) : ICommandHandler<SetGradeTopicOrder>
{
    public async Task HandleAsync(
        SetGradeTopicOrder command, CancellationToken cancellationToken = default)
    {
        logger.LogDebug(
            "Handling SetGradeTopicOrder assignment {AssignmentId} to position {Order}",
            command.AssignmentId, command.Order);

        // The route is id-only: the grade (and therefore the scope) comes from the
        // tenant-filtered row itself. A subject assignment is a TPH subtype, so an
        // activity-group id is not a grade subject assignment either.
        var candidate = await repository.GetAsync(command.AssignmentId, cancellationToken);
        if (candidate is not GradeTopicAssignment gradeRow)
        {
            throw new InvalidOperationException(
                $"Grade topic assignment '{command.AssignmentId}' was not found.");
        }

        // Tracked load, restricted to the EFFECTIVE set — the same rows the
        // curriculum read lists and the same bounds the out-of-range check uses.
        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var rows = await repository.ListByGradeLevelForUpdateAsync(
            gradeRow.GradeLevelId, effectiveDate, cancellationToken);

        var mover = rows.FirstOrDefault(x => x.Id == command.AssignmentId)
            ?? throw new InvalidOperationException(
                $"Grade topic assignment '{command.AssignmentId}' is not effective today and cannot be reordered.");

        // Normalise: sort by (DisplayOrder, CreatedAt, Id) and renumber 0..n-1, so a
        // backfill duplicate cannot derail the swap.
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
                $"Order must be within [0, {ordered.Length - 1}] for grade level '{mover.GradeLevelId}'.");
        }

        // Swap: the mover takes the requested position, the assignment that held it
        // takes the mover's old position. No other row moves.
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
            throw new ConcurrencyException("GradeTopicAssignment", mover.Id);
        }

        await cache.RemoveByTagAsync("students", cancellationToken);
        logger.LogInformation(
            "Subject assignment {AssignmentId} moved to position {Order} for grade {GradeLevelId}",
            mover.Id, command.Order, mover.GradeLevelId);
    }
}
