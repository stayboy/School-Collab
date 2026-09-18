namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// Thrown (ar-20) when a create-assignment write carries a <c>teacher_id</c> claim whose
/// teacher does not exist in the teacher directory (students-api). Typed — never a bare
/// <see cref="InvalidOperationException"/> — so the API boundary can map it distinctly.
/// </summary>
public sealed class UnknownTeacherException : Exception
{
    public UnknownTeacherException(Guid teacherId)
        : base($"Teacher with ID '{teacherId}' could not be resolved in the teacher directory.") { }
}
