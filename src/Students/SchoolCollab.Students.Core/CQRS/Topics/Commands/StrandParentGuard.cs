using SchoolCollab.Students.Core.Data;

namespace SchoolCollab.Students.Core.CQRS.Topics.Commands;

/// <summary>
/// Enforces the strand-parenting invariants (strand-lesson-unification-plan.md):
/// a strand can have at most one parent, the parent must be a <b>root</b> strand
/// (a lesson cannot be a parent — no deep nesting), must belong to the same topic,
/// and must not be the strand itself.
/// </summary>
internal static class StrandParentGuard
{
    public static async Task EnsureValidParentAsync(
        StudentsDbContext db,
        Guid parentId,
        Guid topicId,
        Guid? strandId,
        CancellationToken ct)
    {
        var parent = await db.TopicStrands.FindAsync(new object[] { parentId }, ct);
        if (parent == null)
            throw new KeyNotFoundException($"Parent strand {parentId} not found.");
        if (parent.TopicId != topicId)
            throw new InvalidOperationException("Parent strand must belong to the same topic.");
        if (parent.ParentStrandId != null)
            throw new InvalidOperationException("A lesson (parented strand) cannot be a parent — use a root strand.");
        if (strandId.HasValue && parent.Id == strandId.Value)
            throw new InvalidOperationException("A strand cannot be its own parent.");
    }
}
