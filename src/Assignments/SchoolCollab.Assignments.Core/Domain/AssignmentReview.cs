namespace SchoolCollab.Assignments.Core.Domain;

public sealed class AssignmentReview
{
    private AssignmentReview() { }

    internal AssignmentReview(Guid assignmentId, Guid teacherId, decimal? score, string? comments)
    {
        Id = Guid.NewGuid();
        AssignmentId = assignmentId;
        TeacherId = teacherId;
        Score = score;
        Comments = comments;
        ReviewDate = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid AssignmentId { get; private set; }
    public Guid TeacherId { get; private set; }
    public decimal? Score { get; private set; }
    public string? Comments { get; private set; }
    public DateTimeOffset ReviewDate { get; private set; }
}