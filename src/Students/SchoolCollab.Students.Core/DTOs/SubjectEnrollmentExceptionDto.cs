using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// A subject enrollment exception (subject-period-exception-model.md v3 §2): the
/// subject is <b>not</b> offered for <see cref="Division"/> + the
/// <see cref="StartDate"/>/<see cref="EndDate"/> span. There is no period id, no
/// period name and no period status — an exception never references a period
/// instance (§0 decision 7), so nothing here can go stale on year rollover.
///
/// <para><see cref="Division"/> is carried as a string, mirroring
/// <see cref="PeriodDto.Division"/> / <see cref="ActivityGroupDto.Span"/>: enums are
/// projected to their names at the DTO boundary. A null <see cref="StartDate"/> means
/// an open start, a null <see cref="EndDate"/> an open end — at least one is set
/// (§2.2).</para>
/// </summary>
public sealed record SubjectEnrollmentExceptionDto(
    Guid Id,
    Guid? GradeLevelId,
    Guid? ActivityGroupId,
    Guid TopicId,
    string Division,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
