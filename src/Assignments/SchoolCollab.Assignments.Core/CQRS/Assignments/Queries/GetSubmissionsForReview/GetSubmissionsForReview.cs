using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetSubmissionsForReview;

/// <summary>
/// Teacher review queue: submissions for assignments owned by
/// <see cref="GetSubmissionsForReview.TeacherId"/> (spec §4.13).
///
/// <para>Round <c>teacher-scope-auth</c> D3 / [P1-4]: <paramref name="Scope"/> is the caller's
/// resolved <see cref="TeacherScope"/>. A scoped caller also sees the grades/subjects they teach;
/// an <b>empty</b> scope (a teacher-only principal with no usable <c>teacher_id</c> claim, or an
/// unresolved taught set) yields an empty queue — so the route's dev <c>teacherId</c> fallback can
/// never widen it. <c>null</c> means unrestricted (the pre-existing callers' shape).</para>
/// </summary>
public sealed record GetSubmissionsForReview(Guid TeacherId, TeacherScope? Scope = null) : IQuery<SubmissionForReviewDto[]>;
