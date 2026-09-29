using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignGradeTopic;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignActivityGroupTopic;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Rev. 6 FR-55/56/57 are now <b>RETIRED on the bridge</b> — the round doc
/// documents/specs/subject-period-exception-model.md removed the period meaning
/// from <c>TopicAssignment</c> on 2026-09-26. These tests pin the deprecation:
/// both create paths still accept <c>PeriodId</c> on the wire (back-compat) but
/// ignore it, and "not offered in period P" now belongs to
/// <c>SubjectEnrollmentException</c> (see <c>SubjectAvailabilityTests</c> /
/// <c>CreateSubjectEnrollmentExceptionHandlerTests</c>).
/// </summary>
[TestClass]
public class TopicAssignmentPeriodTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static readonly Guid GradeId = Guid.Parse("aaaaaaa1-1111-1111-1111-111111111111");
    private static readonly Guid TopicId = Guid.Parse("aaaaaaa2-2222-2222-2222-222222222222");

    private static CreatePeriodHandler NewCreatePeriod(StudentsTestScope s) => new(
        s.Periods, s.Cache, s.Tenants,
        NullLogger<CreatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s) => new(
        s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache,
        NullLogger<ActivatePeriodHandler>.Instance, StudentsTestScope.Config(10000));

    private static AssignGradeTopicHandler NewAssignGrade(StudentsTestScope s) => new(
        s.GradeTopicAssignments, s.Cache, NullLogger<AssignGradeTopicHandler>.Instance);

    private static AssignActivityGroupTopicHandler NewAssignGroup(StudentsTestScope s) => new(
        new ActivityGroupTopicAssignmentRepository(s.Db), s.Cache,
        NullLogger<AssignActivityGroupTopicHandler>.Instance);

    /// <summary>Seeds an active academic year (and, when <paramref name="withTerm"/>
    /// is true, an active Term within it), plus a GradeLevel row for FK integrity.</summary>
    private static async Task<(Guid yearId, Guid? termId)> SeedActiveYearAsync(StudentsTestScope s, bool withTerm = true)
    {
        var create = NewCreatePeriod(s);
        var yearId = (await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        Guid? termId = null;
        // Guard (FR-G1): seed a Draft term before the Terms year activates. The
        // year then auto-activates the earliest sub (FR-H4a); activating it again
        // is a harmless no-op.
        if (withTerm)
        {
            termId = (await create.HandleAsync(new CreatePeriod(
                "T1", D(2026, 9, 1), D(2026, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        }
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        if (termId is not null)
        {
            await NewActivate(s).HandleAsync(new ActivatePeriod(termId.Value));
        }
        s.Db.GradeLevels.Add(GradeLevel.Create(GradeId, 1, "Grade 1", 1));
        await s.Db.SaveChangesAsync();
        return (yearId, termId);
    }

    /// <summary>Creates a second, NOT-active academic year and one Term inside it.</summary>
    private static async Task<Guid> SeedOtherYearTermAsync(StudentsTestScope s)
    {
        var create = NewCreatePeriod(s);
        var otherYear = (await create.HandleAsync(new CreatePeriod(
            "AY2027", D(2027, 9, 1), D(2028, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        return (await create.HandleAsync(new CreatePeriod(
            "T9", D(2027, 9, 1), D(2027, 12, 31), AcademicYearDivision.Terms, ParentPeriodId: otherYear))).YearId;
    }

    private static async Task<ActivityGroup> SeedGroupAsync(StudentsTestScope s, EnrollmentSpan span)
    {
        var group = ActivityGroup.Create(span + " Club", span: span);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        return group;
    }

    // ── Grade path: PeriodId is accepted and ignored ──────────────────────────

    [TestMethod]
    public async Task AssignGrade_WithAcademicYearPeriodId_IsAcceptedAndIgnored()
    {
        using var s = new StudentsTestScope("tp-grade-year-" + Guid.NewGuid());
        var (yearId, _) = await SeedActiveYearAsync(s);

        var id = await NewAssignGrade(s).HandleAsync(new AssignGradeTopic(
            GradeId, TopicId, D(2026, 9, 1), PeriodId: yearId));

        (await s.GradeTopicAssignments.GetAsync(id))!.PeriodId.Should().BeNull(
            "the bridge row carries no period meaning any more (2026-09-26)");
    }

    [TestMethod]
    public async Task AssignGrade_WithTermPeriodId_IsAcceptedAndIgnored()
    {
        using var s = new StudentsTestScope("tp-grade-term-" + Guid.NewGuid());
        var (_, termId) = await SeedActiveYearAsync(s);

        var id = await NewAssignGrade(s).HandleAsync(new AssignGradeTopic(
            GradeId, TopicId, D(2026, 9, 1), PeriodId: termId));

        (await s.GradeTopicAssignments.GetAsync(id))!.PeriodId.Should().BeNull();
    }

    [TestMethod]
    public async Task AssignGrade_WithTermOutsideActiveYear_IsAcceptedAndIgnored()
    {
        using var s = new StudentsTestScope("tp-grade-ec24-" + Guid.NewGuid());
        await SeedActiveYearAsync(s);
        var otherTerm = await SeedOtherYearTermAsync(s);

        // The retired FR-57 rejection no longer fires: the field is ignored, not validated.
        var id = await NewAssignGrade(s).HandleAsync(new AssignGradeTopic(
            GradeId, TopicId, D(2027, 9, 1), PeriodId: otherTerm));

        (await s.GradeTopicAssignments.GetAsync(id))!.PeriodId.Should().BeNull();
    }

    // ── Group path: PeriodId is accepted and ignored ──────────────────────────

    [TestMethod]
    public async Task AssignGroup_TermlyGroup_WithTermPeriodId_IsAcceptedAndIgnored()
    {
        using var s = new StudentsTestScope("tp-group-term-" + Guid.NewGuid());
        var (_, termId) = await SeedActiveYearAsync(s);
        var group = await SeedGroupAsync(s, EnrollmentSpan.Termly);

        var id = await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 9, 1), PeriodId: termId));

        (await new ActivityGroupTopicAssignmentRepository(s.Db).GetAsync(id))!.PeriodId.Should().BeNull(
            "the group write path retires the whitelist exactly as the grade path does");
    }

    [TestMethod]
    public async Task AssignGroup_TermlyGroup_WithTermOfNonActiveYear_IsAcceptedAndIgnored()
    {
        using var s = new StudentsTestScope("tp-group-fr14-" + Guid.NewGuid());
        await SeedActiveYearAsync(s);
        var otherTerm = await SeedOtherYearTermAsync(s);
        var group = await SeedGroupAsync(s, EnrollmentSpan.Termly);

        var id = await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2027, 9, 1), PeriodId: otherTerm));

        (await new ActivityGroupTopicAssignmentRepository(s.Db).GetAsync(id))!.PeriodId.Should().BeNull();
    }

    [TestMethod]
    public async Task AssignGroup_OpenEndedGroup_WithPeriodId_IsAcceptedAndIgnored()
    {
        using var s = new StudentsTestScope("tp-group-ec23-" + Guid.NewGuid());
        var (yearId, _) = await SeedActiveYearAsync(s);
        var group = await SeedGroupAsync(s, EnrollmentSpan.OpenEnded);

        var id = await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 9, 1), PeriodId: yearId));

        (await new ActivityGroupTopicAssignmentRepository(s.Db).GetAsync(id))!.PeriodId.Should().BeNull();
    }

    [TestMethod]
    public async Task AssignGroup_TermlyGroup_WithAcademicYearPeriodId_IsAcceptedAndIgnored()
    {
        using var s = new StudentsTestScope("tp-group-mismatch-" + Guid.NewGuid());
        var (yearId, _) = await SeedActiveYearAsync(s);
        var group = await SeedGroupAsync(s, EnrollmentSpan.Termly);

        var id = await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 9, 1), PeriodId: yearId));

        (await new ActivityGroupTopicAssignmentRepository(s.Db).GetAsync(id))!.PeriodId.Should().BeNull();
    }

    // ── Duplicate guard: now (group, topic) — the database's own uniqueness ───

    [TestMethod]
    public async Task AssignGroup_DuplicateActiveSamePeriod_Throws()
    {
        using var s = new StudentsTestScope("tp-dup-period-" + Guid.NewGuid());
        var (_, termId) = await SeedActiveYearAsync(s);
        var group = await SeedGroupAsync(s, EnrollmentSpan.Termly);

        // Start date in the past so the assignment is active on today (the guard
        // checks effectiveness on DateTime.UtcNow).
        await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 1, 1), PeriodId: termId));

        await FluentActions.Awaiting(() => NewAssignGroup(s).HandleAsync(
            new AssignActivityGroupTopic(group.Id, TopicId, D(2026, 1, 1), PeriodId: termId)))
            .Should().ThrowAsync<DuplicateTopicAssignmentException>();
    }

    [TestMethod]
    public async Task AssignGroup_DuplicateActiveNullPeriod_Throws()
    {
        using var s = new StudentsTestScope("tp-dup-null-" + Guid.NewGuid());
        await SeedActiveYearAsync(s);
        var group = await SeedGroupAsync(s, EnrollmentSpan.OpenEnded);

        await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 1, 1)));

        await FluentActions.Awaiting(() => NewAssignGroup(s).HandleAsync(
            new AssignActivityGroupTopic(group.Id, TopicId, D(2026, 1, 1))))
            .Should().ThrowAsync<DuplicateTopicAssignmentException>();
    }

    [TestMethod]
    public async Task AssignGroup_SameTopicWithTwoDifferentPeriodIds_IsStillADuplicate()
    {
        // Rev. 6 allowed a second bridge row for the same (group, topic) when the
        // PeriodId differed. That premise is unreachable: PeriodId is now ignored
        // (one row per (tenant, group, topic), which
        // ix_topic_assignments_tenant_group_topic_unique already enforced), so the
        // second request is a 409 instead of a raw unique-index violation.
        using var s = new StudentsTestScope("tp-diff-period-" + Guid.NewGuid());
        var (yearId, termId) = await SeedActiveYearAsync(s);
        var group = await SeedGroupAsync(s, EnrollmentSpan.Termly);

        var create = NewCreatePeriod(s);
        var term2 = (await create.HandleAsync(new CreatePeriod(
            "T2", D(2027, 1, 1), D(2027, 4, 30), AcademicYearDivision.Terms, ParentPeriodId: yearId))).YearId;
        await NewActivate(s).HandleAsync(new ActivatePeriod(term2));

        await NewAssignGroup(s).HandleAsync(new AssignActivityGroupTopic(
            group.Id, TopicId, D(2026, 1, 1), PeriodId: termId));

        await FluentActions.Awaiting(() => NewAssignGroup(s).HandleAsync(
            new AssignActivityGroupTopic(group.Id, TopicId, D(2026, 1, 1), PeriodId: term2)))
            .Should().ThrowAsync<DuplicateTopicAssignmentException>(
                "a differing (ignored) PeriodId can no longer buy a second row");
    }
}
