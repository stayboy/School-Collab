using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Data.Repositories;

/// <summary>
/// Repository for <see cref="SubjectEnrollmentException"/> — the date-span exception
/// that hides an otherwise-assigned subject for a period part / window
/// (subject-period-exception-model.md v3 §2). Exceptions are written rarely and read
/// rarely; nothing here is cached.
/// </summary>
public interface ISubjectEnrollmentExceptionRepository
{
    Task<SubjectEnrollmentException?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(SubjectEnrollmentException exception, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a batch and saves ONCE, so the batch is one transaction — every row or none
    /// (subject-period-exception-model.md v6 §11.3). Mirrors
    /// <c>CodedValueRepository.AddRangeAsync</c>, the repo's existing bulk-create idiom.
    /// </summary>
    Task AddRangeAsync(IEnumerable<SubjectEnrollmentException> exceptions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes the exception (removing one is a delete, never a status change).
    /// The row is retained as audit: "who excepted Math for Term 3, and when".
    /// </summary>
    Task SoftDeleteAsync(SubjectEnrollmentException exception, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists exceptions for an owner, optionally narrowed to one topic. Both owner
    /// filters null lists every exception of the current tenant.
    /// </summary>
    Task<SubjectEnrollmentExceptionDto[]> ListDtosAsync(
        Guid? gradeLevelId,
        Guid? activityGroupId,
        Guid? topicId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when a live exception already exists for the exact
    /// <c>(owner, topic, division, span)</c> — backs the 409 duplicate guard. Null
    /// bounds are compared as nulls (not as SQL unknowns), mirroring the
    /// <c>COALESCE</c> the unique expression index applies.
    /// </summary>
    Task<bool> ExistsAsync(
        Guid? gradeLevelId,
        Guid? activityGroupId,
        Guid topicId,
        AcademicYearDivision division,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken = default);
}
