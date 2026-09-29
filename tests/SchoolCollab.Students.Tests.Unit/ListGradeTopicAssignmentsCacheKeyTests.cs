using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.CreateSubjectEnrollmentException;
using SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.RemoveSubjectEnrollmentException;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListActivityGroupTopicAssignments;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicAssignments;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicCurriculumByGrade;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// HybridCache behaviour of the two cached availability readers (the FR-58 feed readers)
/// and of the curriculum reader (the management surface, which no longer filters on
/// exceptions), after the key reverted to <b>date-only</b>
/// (subject-period-exception-model.md v3 §4.3). Availability is a
/// single date test, so the date is the key's only varying dimension and the failure mode
/// v1's active-period segment guarded against is structurally gone (§0 decision 9).
///
/// <para><b>Retired v1 tests and their replacements</b> (RISK-2's cache-key half):
/// <c>GradeReader_SameEffectiveDate_DifferentActivePeriod_DoesNotServeTheStaleArray</c>,
/// <c>GradeReader_DifferentActiveSubPeriod_IsAlsoPartOfTheKey</c>,
/// <c>GroupReader_SameEffectiveDate_DifferentActivePeriod_DoesNotServeTheStaleArray</c> and
/// <c>CurriculumReader_SameEffectiveDate_DifferentActivePeriod_DoesNotServeTheStaleArray</c>
/// retire with the period segment; the surviving key dimension (the effective date) is
/// pinned by <c>GradeReader_DifferentEffectiveDate_IsKeyedSeparately</c>, and the write
/// paths' tag invalidation — the property that made the period segment unnecessary — is
/// pinned by the four invalidation tests below.
/// <c>CurriculumReader_ExceptionCreated_InvalidatesSoTheNextReadExcludes</c> is replaced by
/// <c>CurriculumReader_ExceptionCreated_StillListsTheTopic_AndTheTagWasEvicted</c>: the
/// curriculum reader is the management surface and no longer filters on exceptions
/// (spec §5.2 rows 3-4/AC-9), so the surviving half is the tag eviction, observed on the
/// same reader.</para>
/// </summary>
[TestClass]
public class ListGradeTopicAssignmentsCacheKeyTests
{
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static SubjectEnrollmentException SeedGradeException(
        StudentsTestScope s, Guid gradeId, Guid topicId, DateOnly? start, DateOnly? end)
    {
        var exception = SubjectEnrollmentException.Create(
            s.Tenants.GetTenantContext().TenantId, gradeId, null, topicId,
            AcademicYearDivision.None, start, end);
        s.Db.SubjectEnrollmentExceptions.Add(exception);
        return exception;
    }

    /// <summary>Grade + topic + a period-less bridge row (the post-round normal state).</summary>
    private static async Task<(Guid gradeId, Guid topicId)> SeedAvailableGradeTopicAsync(StudentsTestScope s)
    {
        var gradeLevel = GradeLevel.Create(Guid.NewGuid(), 1, "Grade 1", 1);
        s.Db.GradeLevels.Add(gradeLevel);
        var topic = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        s.Db.Topics.Add(topic);
        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(gradeLevel.Id, topic.Id, Today()));
        await s.Db.SaveChangesAsync();
        return (gradeLevel.Id, topic.Id);
    }

    private static CreateSubjectEnrollmentExceptionHandler NewCreate(StudentsTestScope s) => new(
        new SubjectEnrollmentExceptionRepository(s.Db),
        s.GradeLevels,
        s.ActivityGroups,
        s.Topics,
        s.Tenants,
        s.Cache,
        NullLogger<CreateSubjectEnrollmentExceptionHandler>.Instance);

    private static RemoveSubjectEnrollmentExceptionHandler NewRemove(StudentsTestScope s) => new(
        new SubjectEnrollmentExceptionRepository(s.Db),
        s.Cache,
        NullLogger<RemoveSubjectEnrollmentExceptionHandler>.Instance);

    // ── the one remaining key dimension: the effective date ──────────────────

    [TestMethod]
    public async Task GradeReader_DifferentEffectiveDate_IsKeyedSeparately()
    {
        using var s = new StudentsTestScope("cachekey-grade-date");
        var (gradeId, topicId) = await SeedAvailableGradeTopicAsync(s);
        // The exception contains only the LATER date.
        SeedGradeException(s, gradeId, topicId, Today().AddDays(10), Today().AddDays(20));
        await s.Db.SaveChangesAsync();

        var handler = new ListGradeTopicAssignmentsHandler(s.Db, s.Cache);

        (await handler.HandleAsync(new ListGradeTopicAssignments(gradeId, Today())))
            .Select(a => a.TopicId).Should().Contain(topicId, "the exception does not contain today");

        (await handler.HandleAsync(new ListGradeTopicAssignments(gradeId, Today().AddDays(12))))
            .Select(a => a.TopicId).Should().BeEmpty(
            "a different effective date must not be served the other date's array");
    }

    // ── invalidation on exception writes (AC-13, carried from v1) ────────────

    [TestMethod]
    public async Task GradeReader_ExceptionCreated_InvalidatesSoTheNextReadExcludes()
    {
        using var s = new StudentsTestScope("cachekey-grade-create");
        var (gradeId, topicId) = await SeedAvailableGradeTopicAsync(s);
        var handler = new ListGradeTopicAssignmentsHandler(s.Db, s.Cache);

        (await handler.HandleAsync(new ListGradeTopicAssignments(gradeId, Today())))
            .Select(a => a.TopicId).Should().Contain(topicId);

        await NewCreate(s).HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, topicId, AcademicYearDivision.None, Today().AddDays(-1), Today().AddDays(1)));

        (await handler.HandleAsync(new ListGradeTopicAssignments(gradeId, Today())))
            .Should().BeEmpty("creating an exception evicts the \"students\" tag, so the reader recomputes");
    }

    [TestMethod]
    public async Task GradeReader_ExceptionRemoved_InvalidatesSoTheNextReadIncludes()
    {
        using var s = new StudentsTestScope("cachekey-grade-remove");
        var (gradeId, topicId) = await SeedAvailableGradeTopicAsync(s);
        var handler = new ListGradeTopicAssignmentsHandler(s.Db, s.Cache);
        var exception = SeedGradeException(s, gradeId, topicId, Today().AddDays(-1), Today().AddDays(1));
        await s.Db.SaveChangesAsync();

        (await handler.HandleAsync(new ListGradeTopicAssignments(gradeId, Today())))
            .Select(a => a.TopicId).Should().BeEmpty();

        await NewRemove(s).HandleAsync(new RemoveSubjectEnrollmentException(exception.Id));

        (await handler.HandleAsync(new ListGradeTopicAssignments(gradeId, Today())))
            .Select(a => a.TopicId).Should().Contain(topicId);
    }

    [TestMethod]
    public async Task GroupReader_ExceptionCreated_InvalidatesSoTheNextReadExcludes()
    {
        using var s = new StudentsTestScope("cachekey-group-create");
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        var topic = Topic.Create(Guid.NewGuid(), "ART", "Art", 1);
        s.Db.Topics.Add(topic);
        s.Db.ActivityGroupTopicAssignments.Add(ActivityGroupTopicAssignment.Create(group.Id, topic.Id, Today()));
        await s.Db.SaveChangesAsync();

        var handler = new ListActivityGroupTopicAssignmentsHandler(s.Db, s.Cache);

        (await handler.HandleAsync(new ListActivityGroupTopicAssignments(group.Id, Today())))
            .Select(a => a.TopicId).Should().Contain(topic.Id);

        await NewCreate(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, topic.Id, AcademicYearDivision.Terms, Today().AddDays(-1), Today().AddDays(1)));

        (await handler.HandleAsync(new ListActivityGroupTopicAssignments(group.Id, Today())))
            .Should().BeEmpty();
    }

    [TestMethod]
    public async Task CurriculumReader_ExceptionCreated_StillListsTheTopic_AndTheTagWasEvicted()
    {
        // Replaces v1's CurriculumReader_ExceptionCreated_InvalidatesSoTheNextReadExcludes.
        // P1-1 (parent ruling, 2026-09-26): the curriculum reader is the MANAGEMENT surface,
        // so an exception no longer removes the topic from it — the grade-detail card and the
        // View-all dialog need the subject listed to render its badge and kebab (spec §5.2
        // rows 3-4/AC-9). The invalidation intent survives and is still observed on THIS reader:
        // the bridge row added after the exception write only shows up if the exception write
        // evicted the "students" tag.
        using var s = new StudentsTestScope("cachekey-curriculum-create");
        var (gradeId, topicId) = await SeedAvailableGradeTopicAsync(s);
        var handler = new ListGradeTopicCurriculumByGradeHandler(s.Db, s.Cache);

        (await handler.HandleAsync(new ListGradeTopicCurriculumByGrade(gradeId, Today())))
            .Select(x => x.TopicId).Should().Contain(topicId);

        await NewCreate(s).HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, topicId, AcademicYearDivision.None, Today().AddDays(-1), Today().AddDays(1)));

        // Written straight to the DbContext, so only the exception write's tag eviction
        // (AC-13) can make it visible to the next read.
        var secondTopic = Topic.Create(Guid.NewGuid(), "ENG", "English", 2);
        s.Db.Topics.Add(secondTopic);
        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(gradeId, secondTopic.Id, Today()));
        await s.Db.SaveChangesAsync();

        var recomputed = await handler.HandleAsync(new ListGradeTopicCurriculumByGrade(gradeId, Today()));
        recomputed.Select(x => x.TopicId).Should().Contain(secondTopic.Id,
            "AC-13: creating an exception evicts the \"students\" tag, so a management read after the write recomputes");
        recomputed.Select(x => x.TopicId).Should().Contain(topicId,
            "the management read keeps listing the excepted subject — the exception filter lives on the FR-58 feed readers");
    }
}
