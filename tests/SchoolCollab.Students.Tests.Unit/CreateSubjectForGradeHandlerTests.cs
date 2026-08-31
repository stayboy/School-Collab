using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicForGrade;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class CreateTopicForGradeHandlerTests
{
    private static CreateTopicForGradeHandler NewHandler(StudentsTestScope s, IEntityCodeGenerator? gen = null) =>
        new(
            s.Topics,
            s.GradeTopicAssignments,
            s.GradeLevels,
            s.Periods,
            s.Cache,
            s.Tenants,
            gen ?? new Mock<IEntityCodeGenerator>().Object,
            NullLogger<CreateTopicForGradeHandler>.Instance);

    private static async Task<Guid> SeedGradeLevelAsync(StudentsTestScope s, Guid codedValueId, int level, string name)
    {
        var gl = GradeLevel.Create(codedValueId, level, name, level);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    private static async Task<Guid> SeedCurrentPeriodAsync(StudentsTestScope s, string name)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var period = Period.Create(name, today.AddDays(-1), today.AddDays(1), AcademicYearDivision.None);
        s.Db.Periods.Add(period);
        await s.Db.SaveChangesAsync();
        return period.Id;
    }

    private static async Task<Guid> SeedActiveYearAndTermAsync(StudentsTestScope s)
    {
        var create = new CreatePeriodHandler(
            s.Periods, s.Cache, s.Tenants,
            NullLogger<CreatePeriodHandler>.Instance);
        var ay = (await create.HandleAsync(new CreatePeriod("AY2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var term = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31),
            AcademicYearDivision.Terms, ParentPeriodId: ay))).YearId;
        var activate = new ActivatePeriodHandler(
            s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache,
            NullLogger<ActivatePeriodHandler>.Instance);
        await activate.HandleAsync(new ActivatePeriod(ay));
        await activate.HandleAsync(new ActivatePeriod(term));
        return term;
    }

    [TestMethod]
    public async Task CreateForGrade_CreatesTopicAndAssignment()
    {
        using var s = new StudentsTestScope("csfg-create");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");
        var h = NewHandler(s);

        var dto = await h.HandleAsync(new CreateTopicForGrade(gradeId, CodedValueId: null, "MATH", "Mathematics", 1));

        dto.Id.Should().NotBeEmpty();
        dto.Code.Should().Be("MATH");
        dto.Name.Should().Be("Mathematics");
        (await s.Db.Topics.CountAsync()).Should().Be(1);
        (await s.Db.GradeTopicAssignments.CountAsync()).Should().Be(1);
        var assignment = await s.Db.GradeTopicAssignments.FirstAsync();
        assignment.GradeLevelId.Should().Be(gradeId);
        assignment.TopicId.Should().Be(dto.Id);
        // Assignments are date-based, not period-bound: the bridge opens today
        // and stays open-ended (EndDate = null) so the topic spans multiple years.
        assignment.StartDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        assignment.EndDate.Should().BeNull();
    }

    [TestMethod]
    public async Task CreateForGrade_ReusesExistingTopicByCodedValueId()
    {
        using var s = new StudentsTestScope("csfg-reuse");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");
        var periodId = await SeedCurrentPeriodAsync(s, "Term 1");

        // Pre-existing topic with the same CodedValueId
        var existing = Topic.Create(cv, "ENG", "English", 2);
        s.Db.Topics.Add(existing);
        await s.Db.SaveChangesAsync();

        var h = NewHandler(s);

        var dto = await h.HandleAsync(new CreateTopicForGrade(gradeId, CodedValueId: cv, "ENG", "English Updated", 5));

        // Reuses the existing subject, updates mirrored name
        dto.Id.Should().Be(existing.Id);
        dto.Name.Should().Be("English Updated");
        (await s.Db.Topics.CountAsync()).Should().Be(1);
        (await s.Db.GradeTopicAssignments.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task CreateForGrade_IsIdempotentForAssignment()
    {
        using var s = new StudentsTestScope("csfg-idempotent");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");
        await SeedCurrentPeriodAsync(s, "Term 1");
        var h = NewHandler(s);

        await h.HandleAsync(new CreateTopicForGrade(gradeId, null, "MATH", "Mathematics", 1));
        await h.HandleAsync(new CreateTopicForGrade(gradeId, null, "MATH", "Mathematics", 1));

        // Same code → find-or-create reuses the same subject, and the assignment
        // is not duplicated.
        (await s.Db.Topics.CountAsync()).Should().Be(1);
        (await s.Db.GradeTopicAssignments.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task CreateForGrade_WorksWithoutAnyPeriod()
    {
        // Assignments are date-based, not period-bound, so creating a topic for a
        // grade must NOT require a current period. The bridge opens today and is
        // open-ended (EndDate = null).
        using var s = new StudentsTestScope("csfg-no-period");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");
        // No period seeded
        var h = NewHandler(s);

        var dto = await h.HandleAsync(new CreateTopicForGrade(gradeId, null, "MATH", "Mathematics", 1));

        dto.Id.Should().NotBeEmpty();
        (await s.Db.GradeTopicAssignments.CountAsync()).Should().Be(1);
        var assignment = await s.Db.GradeTopicAssignments.FirstAsync();
        assignment.EndDate.Should().BeNull();
    }

    [TestMethod]
    public async Task CreateForGrade_WithPeriodId_ScopesAssignmentToPeriod()
    {
        // Rev. 6 FR-55/57: a grade-owned topic's PeriodId, when set, must be an
        // AcademicYear or a Term/Semester within the active academic year. The
        // created assignment must carry that PeriodId (no duplicate assignment).
        using var s = new StudentsTestScope("csfg-period");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");
        var termId = await SeedActiveYearAndTermAsync(s);
        var h = NewHandler(s);

        var dto = await h.HandleAsync(new CreateTopicForGrade(gradeId, null, "MATH", "Mathematics", 1, PeriodId: termId));

        (await s.Db.GradeTopicAssignments.CountAsync()).Should().Be(1);
        var assignment = await s.Db.GradeTopicAssignments.FirstAsync();
        assignment.TopicId.Should().Be(dto.Id);
        assignment.PeriodId.Should().Be(termId);
    }

    [TestMethod]
    public async Task CreateForGrade_WithTermOutsideActiveYear_Throws()
    {
        // Rev. 6 EC-24: a grade topic PeriodId that is a Term outside the active
        // academic year is rejected.
        using var s = new StudentsTestScope("csfg-period-invalid");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");
        var termId = await SeedActiveYearAndTermAsync(s);
        // A second academic year (not active) with a term inside it.
        var create = new CreatePeriodHandler(
            s.Periods, s.Cache, s.Tenants,
            NullLogger<CreatePeriodHandler>.Instance);
        var otherAy = (await create.HandleAsync(new CreatePeriod("AY2027", new DateOnly(2027, 9, 1), new DateOnly(2028, 8, 31), Division: AcademicYearDivision.Terms))).YearId;
        var otherTerm = (await create.HandleAsync(new CreatePeriod("T1", new DateOnly(2027, 9, 1), new DateOnly(2028, 1, 31),
            AcademicYearDivision.Terms, ParentPeriodId: otherAy))).YearId;
        var h = NewHandler(s);

        var act = async () => await h.HandleAsync(new CreateTopicForGrade(gradeId, null, "MATH", "Mathematics", 1, PeriodId: otherTerm));

        await act.Should().ThrowAsync<TopicAssignmentPeriodException>();
    }

    [TestMethod]
    public async Task CreateForGrade_ExistingAssignmentDifferentPeriod_CreatesScopedAssignment()
    {
        // Rev. 6 FR-55/57: a request scoped to a Term when a year-spanning
        // (PeriodId = null) assignment already exists must create a NEW assignment
        // carrying the requested Term — the idempotency guard is period-scoped.
        using var s = new StudentsTestScope("csfg-diff-period");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");
        var termId = await SeedActiveYearAndTermAsync(s);
        var h = NewHandler(s);

        // First: year-spanning assignment (no period).
        var yearSpanning = await h.HandleAsync(new CreateTopicForGrade(gradeId, null, "MATH", "Mathematics", 1));
        (await s.Db.GradeTopicAssignments.CountAsync()).Should().Be(1);

        // Second: same topic, now scoped to the active Term.
        var scoped = await h.HandleAsync(new CreateTopicForGrade(gradeId, null, "MATH", "Mathematics", 1, PeriodId: termId));

        scoped.Id.Should().Be(yearSpanning.Id, "the shared topic is reused");
        (await s.Db.GradeTopicAssignments.CountAsync()).Should().Be(2, "a differently-scoped request adds a new assignment");
        var termAssignment = await s.Db.GradeTopicAssignments.SingleAsync(a => a.PeriodId == termId);
        termAssignment.TopicId.Should().Be(yearSpanning.Id);
        termAssignment.GradeLevelId.Should().Be(gradeId);
    }

    [TestMethod]
    public async Task CreateForGrade_ExistingSamePeriod_Skips()
    {
        // Rev. 6 FR-55/57: repeating the SAME period-scoped request must not
        // duplicate the assignment — the guard is true idempotency.
        using var s = new StudentsTestScope("csfg-same-period");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");
        var termId = await SeedActiveYearAndTermAsync(s);
        var h = NewHandler(s);

        await h.HandleAsync(new CreateTopicForGrade(gradeId, null, "MATH", "Mathematics", 1, PeriodId: termId));
        await h.HandleAsync(new CreateTopicForGrade(gradeId, null, "MATH", "Mathematics", 1, PeriodId: termId));

        (await s.Db.GradeTopicAssignments.CountAsync()).Should().Be(1, "same period scope is idempotent");
        var assignment = await s.Db.GradeTopicAssignments.SingleAsync();
        assignment.PeriodId.Should().Be(termId);
    }

    [TestMethod]
    public async Task CreateForGrade_ThrowsWhenGradeLevelNotFound()
    {
        using var s = new StudentsTestScope("csfg-no-grade");
        await SeedCurrentPeriodAsync(s, "Term 1");
        var h = NewHandler(s);

        var act = async () => await h.HandleAsync(new CreateTopicForGrade(Guid.NewGuid(), null, "MATH", "Mathematics", 1));

        await act.Should().ThrowAsync<GradeLevelNotFoundException>();
    }

    [TestMethod]
    public async Task CreateForGrade_BlankCode_GeneratesFromNameViaTopicCodeRule()
    {
        // tcv/5: a blank code is generated from the topic name via the TOPIC_CODE
        // entity-code rule (WordInitials + NumericSequence), e.g. "computer science"
        // → CS01. The handler must call GenerateWithNameAsync with the TOPIC_CODE
        // rule and the topic name as the name hint.
        using var s = new StudentsTestScope("csfg-generated");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");

        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateWithNameAsync("TOPIC_CODE", "Computer Science", It.IsAny<CancellationToken>()))
                 .ReturnsAsync("CS01");

        var h = NewHandler(s, generator.Object);

        var dto = await h.HandleAsync(new CreateTopicForGrade(gradeId, CodedValueId: null, Code: null, "Computer Science", 1));

        dto.Code.Should().Be("CS01", "the generated code must be assigned to the topic");
        (await s.Db.Topics.CountAsync()).Should().Be(1);
        generator.Verify(g => g.GenerateWithNameAsync("TOPIC_CODE", "Computer Science", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CreateForGrade_ExplicitCode_DoesNotInvokeGenerator()
    {
        using var s = new StudentsTestScope("csfg-explicit");
        var cv = Guid.NewGuid();
        var gradeId = await SeedGradeLevelAsync(s, cv, 1, "Grade 1");

        var generator = new Mock<IEntityCodeGenerator>();
        var h = NewHandler(s, generator.Object);

        var dto = await h.HandleAsync(new CreateTopicForGrade(gradeId, CodedValueId: null, "MATH", "Mathematics", 1));

        dto.Code.Should().Be("MATH");
        generator.Verify(g => g.GenerateWithNameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}