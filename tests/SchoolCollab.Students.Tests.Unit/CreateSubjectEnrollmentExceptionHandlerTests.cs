using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.SubjectEnrollmentExceptions.Commands.CreateSubjectEnrollmentException;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// <see cref="CreateSubjectEnrollmentExceptionHandler"/> — the exception-shape matrix
/// (subject-period-exception-model.md v3 §2.2/§4.2/§6, AC-2/AC-3/AC-6/AC-7):
/// the span invariants (≥1 bound, End ≥ Start), the FR-56 <b>division</b> matrix for
/// group-owned exceptions (including the DateRange containment rule with null bounds),
/// the deliberately <b>absent</b> FR-57 constraint for grade-owned ones, and a
/// duplicate (owner, topic, division, span) being a 409.
///
/// <para><b>Retired v1 tests and their named replacements</b> (the period reference is
/// gone, so nothing here consults a period):
/// <c>Block_GradeArchivedPeriod_Throws422</c> / <c>Block_GradeDeactivatedPeriod_Throws422</c>
/// / <c>Block_GradeDraftTerm_IsAllowed</c> / <c>Block_GradeActiveTerm_IsAllowed</c> /
/// <c>Block_GradeCompletedTerm_IsAllowed</c> → <c>Create_GradeOwned_AcceptsAnyDivisionAndAnySpan</c>
/// (there is no period status to be admissible or retired, and no period to look up);
/// <c>Block_GradeTermOutsideActiveAcademicYear_Throws422</c> /
/// <c>Block_GradeAcademicYearPeriod_IsAllowedEvenWhenNotTheActiveYear</c> →
/// <c>Create_GradeOwned_FarFutureSpan_IsAccepted</c> (AC-7: FR-57 is unconstrained);
/// <c>Block_TermlyGroup_TermPeriod_IsAllowed</c> / <c>Block_TermlyGroup_SemesterPeriod_Throws422</c>
/// / <c>Block_WholeAcademicYearGroup_AcademicYear_IsAllowed</c> → the
/// <c>Create_*Group_*Division*</c> matrix below (a division, not a period instance);
/// <c>Block_OpenEndedGroup_Throws422</c> / <c>Block_DateRangeGroup_Throws422</c> →
/// <c>Create_OpenEndedGroup_NoDivision_IsAllowed</c> /
/// <c>Create_DateRangeGroup_*</c> (v3 makes both spans exceptable; the containment rule
/// is what remains); <c>Block_UnknownPeriod_Throws422</c> →
/// <c>Create_GradeOwned_AcceptsAnyDivisionAndAnySpan</c> (no period is consulted, so an
/// unknown period id cannot be rejected).</para>
///
/// <para>The boundary guards added by the Pass-2 diff review:
/// <c>Create_UnknownTopic_*</c> / <c>Create_EmptyTopic_ThrowsTopicNotFound</c> (P2-4 — an
/// unknown topic is a 404, not the FK's unhandled <c>DbUpdateException</c>) and
/// <c>Create_UndefinedDivision_Throws422</c> (P2-5 — the int-backed enum must be
/// <c>Enum.IsDefined</c>, so <c>42</c> is a 422 instead of a stored value).</para>
/// </summary>
[TestClass]
public class CreateSubjectEnrollmentExceptionHandlerTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    /// <summary>The topic row every successful create references — seeded by
    /// <see cref="SeedCanonicalTopicAsync"/>.
    /// A <c>Topic</c> generates its own id (the factory takes a coded-value id, not an id),
    /// so the id has to be captured rather than declared.</summary>
    private Guid _topicId;

    private static CreateSubjectEnrollmentExceptionHandler NewHandler(StudentsTestScope s) => new(
        new SubjectEnrollmentExceptionRepository(s.Db),
        s.GradeLevels,
        s.ActivityGroups,
        s.Topics,
        s.Tenants,
        s.Cache,
        NullLogger<CreateSubjectEnrollmentExceptionHandler>.Instance);

    /// <summary>
    /// Seeds the topic row the canonical <see cref="_topicId"/> refers to. Every test that gets
    /// past the owner validation needs it: the handler now 404s an unknown topic id instead of
    /// letting the foreign key raise an unhandled <c>DbUpdateException</c> (P2-4).
    /// </summary>
    private async Task SeedCanonicalTopicAsync(StudentsTestScope s)
    {
        var topic = Topic.Create(Guid.NewGuid(), "MATH", "Mathematics", 1);
        s.Db.Topics.Add(topic);
        await s.Db.SaveChangesAsync();
        _topicId = topic.Id;
    }

    /// <summary>Seeds a GradeLevel row (no period anywhere) and returns its real id.</summary>
    private static async Task<Guid> SeedGradeLevelAsync(StudentsTestScope s)
    {
        var gradeLevel = GradeLevel.Create(Guid.NewGuid(), 1, "Grade 1", 1);
        s.Db.GradeLevels.Add(gradeLevel);
        await s.Db.SaveChangesAsync();
        return gradeLevel.Id;
    }

    private static async Task<ActivityGroup> SeedGroupAsync(
        StudentsTestScope s, EnrollmentSpan span, DateOnly? windowStart = null, DateOnly? windowEnd = null)
    {
        var group = ActivityGroup.Create(
            span + " Club", span: span, enrollmentStartDate: windowStart, enrollmentEndDate: windowEnd);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        return group;
    }

    // ── FR-57: a grade-owned exception is unconstrained (AC-7) ────────────────

    [TestMethod]
    public async Task Create_GradeOwned_AcceptsAnyDivisionAndAnySpan()
    {
        using var s = new StudentsTestScope("exc-grade-any");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);
        var handler = NewHandler(s);

        var openStart = await handler.HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, _topicId, AcademicYearDivision.None, StartDate: null, EndDate: D(2027, 3, 31)));
        var openEnd = await handler.HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, _topicId, AcademicYearDivision.Terms, StartDate: D(2027, 3, 1), EndDate: null));
        var bounded = await handler.HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, _topicId, AcademicYearDivision.Semesters, StartDate: D(2026, 9, 1), EndDate: D(2026, 12, 31)));

        openStart.Should().NotBeEmpty("an open start is a valid span for a grade-owned exception");
        openEnd.Should().NotBeEmpty("an open end is a valid span for a grade-owned exception");
        bounded.Should().NotBeEmpty("any Division is accepted for a grade-owned exception (there is no period to match)");
    }

    [TestMethod]
    public async Task Create_GradeOwned_FarFutureSpan_IsAccepted()
    {
        // AC-7 — the named replacement for v1's retired
        // Block_GradeTermOutsideActiveAcademicYear_Throws422: a grade has no window and
        // no active-year rule, so a span in a year that does not exist yet is accepted.
        using var s = new StudentsTestScope("exc-grade-far-future");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);

        var id = await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, _topicId, AcademicYearDivision.Terms, D(2035, 1, 1), D(2035, 4, 30)));

        id.Should().NotBeEmpty("FR-57 is unconstrained in v3 — there is no active academic year to sit inside");
    }

    // ── §2.2 span shape (AC-2) ───────────────────────────────────────────────

    [TestMethod]
    public async Task Create_WithNoBound_Throws422()
    {
        using var s = new StudentsTestScope("exc-no-bound");
        var gradeId = await SeedGradeLevelAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, _topicId, AcademicYearDivision.None));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("at least one bound");
        s.Db.SubjectEnrollmentExceptions.Should().BeEmpty("a rejected exception persists nothing");
    }

    [TestMethod]
    public async Task Create_WithEndBeforeStart_Throws422()
    {
        using var s = new StudentsTestScope("exc-end-before-start");
        var gradeId = await SeedGradeLevelAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, _topicId, AcademicYearDivision.None, D(2027, 3, 31), D(2027, 1, 1)));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("on or after");
    }

    // ── FR-56: the group-owned division matrix (AC-6) ────────────────────────

    [TestMethod]
    public async Task Create_TermlyGroup_TermsDivision_IsAllowed()
    {
        using var s = new StudentsTestScope("exc-group-termly");
        var group = await SeedGroupAsync(s, EnrollmentSpan.Termly);
        await SeedCanonicalTopicAsync(s);

        var id = await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.Terms, D(2027, 1, 1), D(2027, 3, 31)));

        id.Should().NotBeEmpty("FR-56: Termly requires Terms");
    }

    [TestMethod]
    public async Task Create_TermlyGroup_SemestersDivision_Throws422()
    {
        using var s = new StudentsTestScope("exc-group-termly-mismatch");
        var group = await SeedGroupAsync(s, EnrollmentSpan.Termly);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.Semesters, D(2027, 1, 1), D(2027, 3, 31)));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("requires a Terms");
    }

    [TestMethod]
    public async Task Create_TermlyGroup_NoDivision_Throws422()
    {
        using var s = new StudentsTestScope("exc-group-termly-none");
        var group = await SeedGroupAsync(s, EnrollmentSpan.Termly);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.None, D(2027, 1, 1), D(2027, 3, 31)));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("requires a Terms");
    }

    [TestMethod]
    public async Task Create_SemesterGroup_SemestersDivision_IsAllowed()
    {
        using var s = new StudentsTestScope("exc-group-semester");
        var group = await SeedGroupAsync(s, EnrollmentSpan.Semester);
        await SeedCanonicalTopicAsync(s);

        var id = await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.Semesters, D(2027, 1, 1), D(2027, 6, 30)));

        id.Should().NotBeEmpty("FR-56: Semester requires Semesters");
    }

    [TestMethod]
    public async Task Create_SemesterGroup_TermsDivision_Throws422()
    {
        using var s = new StudentsTestScope("exc-group-semester-mismatch");
        var group = await SeedGroupAsync(s, EnrollmentSpan.Semester);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.Terms, D(2027, 1, 1), D(2027, 3, 31)));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("requires a Semesters");
    }

    [TestMethod]
    public async Task Create_WholeAcademicYearGroup_NoDivision_IsAllowed()
    {
        using var s = new StudentsTestScope("exc-group-year");
        var group = await SeedGroupAsync(s, EnrollmentSpan.WholeAcademicYear);
        await SeedCanonicalTopicAsync(s);

        var id = await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.None, D(2027, 1, 1), D(2027, 6, 30)));

        id.Should().NotBeEmpty("FR-56: WholeAcademicYear requires None");
    }

    [TestMethod]
    public async Task Create_WholeAcademicYearGroup_TermsDivision_Throws422()
    {
        using var s = new StudentsTestScope("exc-group-year-mismatch");
        var group = await SeedGroupAsync(s, EnrollmentSpan.WholeAcademicYear);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.Terms, D(2027, 1, 1), D(2027, 3, 31)));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("requires a None");
    }

    [TestMethod]
    public async Task Create_OpenEndedGroup_NoDivision_IsAllowed()
    {
        // Replaces v1's Block_OpenEndedGroup_Throws422: an OpenEnded group was
        // unexceptable only while a period was the sole expressible shape.
        using var s = new StudentsTestScope("exc-group-openended");
        var group = await SeedGroupAsync(s, EnrollmentSpan.OpenEnded);
        await SeedCanonicalTopicAsync(s);

        var id = await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.None, StartDate: D(2027, 1, 1)));

        id.Should().NotBeEmpty("v3 makes OpenEnded groups exceptable — and an open-ended span is natural for them");
    }

    [TestMethod]
    public async Task Create_OpenEndedGroup_TermsDivision_Throws422()
    {
        using var s = new StudentsTestScope("exc-group-openended-mismatch");
        var group = await SeedGroupAsync(s, EnrollmentSpan.OpenEnded);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.Terms, D(2027, 1, 1)));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("requires a None");
    }

    [TestMethod]
    public async Task Create_DateRangeGroup_SpanInsideWindow_IsAllowed()
    {
        // Replaces v1's Block_DateRangeGroup_Throws422.
        using var s = new StudentsTestScope("exc-group-daterange-inside");
        var group = await SeedGroupAsync(
            s, EnrollmentSpan.DateRange, windowStart: D(2026, 9, 1), windowEnd: D(2027, 8, 31));
        await SeedCanonicalTopicAsync(s);

        var id = await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.None, D(2027, 1, 1), D(2027, 3, 31)));

        id.Should().NotBeEmpty("Q10: a span inside the group's window is admissible");
    }

    [TestMethod]
    public async Task Create_DateRangeGroup_OpenBoundsInsideWindow_IsAllowed()
    {
        // Q10's null-bound half: an exception bound that is open cannot breach a bounded
        // window — the open side simply extends to the window's edge (or beyond it
        // conceptually, but the availability date test is what decides).
        using var s = new StudentsTestScope("exc-group-daterange-open");
        var group = await SeedGroupAsync(
            s, EnrollmentSpan.DateRange, windowStart: D(2026, 9, 1), windowEnd: D(2027, 8, 31));
        await SeedCanonicalTopicAsync(s);
        var handler = NewHandler(s);

        var openStart = await handler.HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.None, StartDate: null, EndDate: D(2027, 1, 31)));
        var openEnd = await handler.HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.None, StartDate: D(2027, 2, 1), EndDate: null));

        openStart.Should().NotBeEmpty("an open start does not start before the window");
        openEnd.Should().NotBeEmpty("an open end does not end after the window");
    }

    [TestMethod]
    public async Task Create_DateRangeGroup_SpanStartingBeforeWindow_Throws422()
    {
        using var s = new StudentsTestScope("exc-group-daterange-early");
        var group = await SeedGroupAsync(
            s, EnrollmentSpan.DateRange, windowStart: D(2026, 9, 1), windowEnd: D(2027, 8, 31));

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.None, D(2026, 1, 1), D(2026, 3, 31)));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("may not start before");
    }

    [TestMethod]
    public async Task Create_DateRangeGroup_SpanEndingAfterWindow_Throws422()
    {
        using var s = new StudentsTestScope("exc-group-daterange-late");
        var group = await SeedGroupAsync(
            s, EnrollmentSpan.DateRange, windowStart: D(2026, 9, 1), windowEnd: D(2027, 8, 31));

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, AcademicYearDivision.None, D(2027, 1, 1), D(2028, 1, 31)));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("may not end after");
    }

    // ── duplicates → 409 (AC-3, handler half) ────────────────────────────────

    [TestMethod]
    public async Task Create_Duplicate_ThrowsDuplicateSubjectEnrollment()
    {
        using var s = new StudentsTestScope("exc-duplicate");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);
        var handler = NewHandler(s);
        var command = new CreateSubjectEnrollmentException(
            gradeId, null, _topicId, AcademicYearDivision.Terms, D(2027, 1, 1), D(2027, 3, 31));

        await handler.HandleAsync(command);

        await FluentActions.Awaiting(() => handler.HandleAsync(command))
            .Should().ThrowAsync<DuplicateSubjectEnrollmentException>();

        s.Db.SubjectEnrollmentExceptions.Should().HaveCount(1, "no silent second row");
    }

    [TestMethod]
    public async Task Create_DuplicateOpenEndedSpan_ThrowsDuplicate()
    {
        // The expression index's whole purpose: both bounds nullable means Postgres
        // treats NULL as distinct, so only the COALESCE index can reject this. Proves
        // the HANDLER half here; the index itself is proven on real Postgres in
        // EnrollmentExceptionAvailabilityEndpointTests.
        using var s = new StudentsTestScope("exc-duplicate-open-ended");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);
        var handler = NewHandler(s);
        var command = new CreateSubjectEnrollmentException(
            gradeId, null, _topicId, AcademicYearDivision.None, StartDate: D(2027, 1, 1), EndDate: null);

        await handler.HandleAsync(command);

        await FluentActions.Awaiting(() => handler.HandleAsync(command))
            .Should().ThrowAsync<DuplicateSubjectEnrollmentException>();

        s.Db.SubjectEnrollmentExceptions.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task Create_SameSpan_DifferentDivision_IsNotADuplicate()
    {
        using var s = new StudentsTestScope("exc-duplicate-division");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);
        var handler = NewHandler(s);

        await handler.HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, _topicId, AcademicYearDivision.Terms, D(2027, 1, 1), D(2027, 3, 31)));
        var second = await handler.HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, _topicId, AcademicYearDivision.None, D(2027, 1, 1), D(2027, 3, 31)));

        second.Should().NotBeEmpty("the uniqueness key includes the division");
    }

    [TestMethod]
    public async Task Create_AfterRemove_CanBeReAdded_DuplicateGuardIgnoresSoftDeletedRows()
    {
        // Proves the HANDLER half only: ExistsAsync runs through the soft-delete query
        // filter, so a soft-deleted row no longer trips the 409 duplicate guard. This
        // suite is EF InMemory, which enforces neither unique indexes nor HasFilter, so
        // it CANNOT prove the COALESCE index's "... AND is_deleted = false" predicate
        // that makes the re-add legal against Postgres — that half is proven in
        // EnrollmentExceptionAvailabilityEndpointTests.
        using var s = new StudentsTestScope("exc-readd");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);
        var handler = NewHandler(s);
        var command = new CreateSubjectEnrollmentException(
            gradeId, null, _topicId, AcademicYearDivision.None, D(2027, 1, 1), D(2027, 3, 31));

        var first = await handler.HandleAsync(command);

        var exception = await s.Db.SubjectEnrollmentExceptions.SingleAsync(x => x.Id == first);
        exception.MarkAsDeleted();
        await s.Db.SaveChangesAsync();

        var second = await handler.HandleAsync(command);

        second.Should().NotBe(first);
    }

    // ── unknown topic (P2-4) and undefined division (P2-5) — boundary guards ───

    [TestMethod]
    public async Task Create_UnknownTopic_ThrowsTopicNotFound()
    {
        // P2-4: an unknown topic id used to reach the FK and surface as an unhandled
        // DbUpdateException (500); the tenant-scoped existence check makes it a 404, the
        // same way an unknown grade or group already was.
        using var s = new StudentsTestScope("exc-unknown-topic");
        var gradeId = await SeedGradeLevelAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, Guid.NewGuid(), AcademicYearDivision.Terms, D(2027, 1, 1)));

        await act.Should().ThrowAsync<TopicNotFoundException>();
        s.Db.SubjectEnrollmentExceptions.Should().BeEmpty("a rejected exception persists nothing");
    }

    [TestMethod]
    public async Task Create_UnknownTopic_ForAGroup_ThrowsTopicNotFound()
    {
        // The topic check sits after the FR-56 division matrix and applies to BOTH owner
        // forms (owner XOR → shape → division → owner existence → topic existence).
        using var s = new StudentsTestScope("exc-unknown-topic-group");
        var group = await SeedGroupAsync(s, EnrollmentSpan.Termly);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, Guid.NewGuid(), AcademicYearDivision.Terms, D(2027, 1, 1)));

        await act.Should().ThrowAsync<TopicNotFoundException>();
    }

    [TestMethod]
    public async Task Create_EmptyTopic_ThrowsTopicNotFound()
    {
        // Guid.Empty would otherwise reach the entity factory's argument guard — an
        // ArgumentException, i.e. another 500 — so the existence check catches it first.
        using var s = new StudentsTestScope("exc-empty-topic");
        var gradeId = await SeedGradeLevelAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, Guid.Empty, AcademicYearDivision.Terms, D(2027, 1, 1)));

        await act.Should().ThrowAsync<TopicNotFoundException>();
    }

    [TestMethod]
    public async Task Create_UndefinedDivision_Throws422()
    {
        // P2-5: the int-backed enum accepts any integer (42 here) and a grade-owned
        // exception would store it verbatim. Enum.IsDefined rejects it at the boundary.
        using var s = new StudentsTestScope("exc-undefined-division");
        var gradeId = await SeedGradeLevelAsync(s);
        await SeedCanonicalTopicAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, null, _topicId, (AcademicYearDivision)42, D(2027, 1, 1)));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("not a valid");
        s.Db.SubjectEnrollmentExceptions.Should().BeEmpty("an undefined division persists nothing");
    }

    [TestMethod]
    public async Task Create_UndefinedDivision_ForAGroup_Throws422()
    {
        // The division check runs before the owner branch, so it gates both owner forms.
        using var s = new StudentsTestScope("exc-undefined-division-group");
        var group = await SeedGroupAsync(s, EnrollmentSpan.Termly);
        await SeedCanonicalTopicAsync(s);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, group.Id, _topicId, (AcademicYearDivision)42, D(2027, 1, 1)));

        await act.Should().ThrowAsync<TopicAssignmentPeriodException>();
    }

    // ── owner shape / not-found guards ────────────────────────────────────────

    [TestMethod]
    public async Task Create_WithBothOwners_Throws422()
    {
        using var s = new StudentsTestScope("exc-two-owners");
        var gradeId = await SeedGradeLevelAsync(s);
        var group = await SeedGroupAsync(s, EnrollmentSpan.Termly);

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            gradeId, group.Id, _topicId, AcademicYearDivision.Terms, D(2027, 1, 1)));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("exactly one owner");
    }

    [TestMethod]
    public async Task Create_WithNeitherOwner_Throws422()
    {
        using var s = new StudentsTestScope("exc-no-owner");

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, null, _topicId, AcademicYearDivision.Terms, D(2027, 1, 1)));

        (await act.Should().ThrowAsync<TopicAssignmentPeriodException>())
            .And.Message.Should().Contain("exactly one owner");
    }

    [TestMethod]
    public async Task Create_UnknownGrade_ThrowsGradeLevelNotFound()
    {
        using var s = new StudentsTestScope("exc-unknown-grade");

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            Guid.NewGuid(), null, _topicId, AcademicYearDivision.Terms, D(2027, 1, 1)));

        await act.Should().ThrowAsync<GradeLevelNotFoundException>();
    }

    [TestMethod]
    public async Task Create_UnknownGroup_ThrowsActivityGroupNotFound()
    {
        using var s = new StudentsTestScope("exc-unknown-group");

        var act = async () => await NewHandler(s).HandleAsync(new CreateSubjectEnrollmentException(
            null, Guid.NewGuid(), _topicId, AcademicYearDivision.None, D(2027, 1, 1)));

        await act.Should().ThrowAsync<ActivityGroupNotFoundException>();
    }
}
