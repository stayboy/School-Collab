using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.AddMembership;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.CreateActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.RolloverActivityGroup;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Period-aligned enrollment spans (spec activity-group-enrollment.md FR-43/45):
/// membership attaches to the matching typed period of the active academic year,
/// and span/framework compatibility gates group creation.
/// </summary>
[TestClass]
public class ActivityGroupPeriodAlignedSpanTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static CreatePeriodHandler NewCreatePeriod(StudentsTestScope s) =>
        NewCreatePeriod(s, "Terms");

    private static CreatePeriodHandler NewCreatePeriod(StudentsTestScope s, string division) => new(
        s.Periods, s.Cache, s.Tenants, new StubAcademicYearDivisionProvider(division),
        NullLogger<CreatePeriodHandler>.Instance);

    private static ActivatePeriodHandler NewActivate(StudentsTestScope s) => new(
        s.Periods, Mock.Of<IIntegrationEventPublisher>(), s.Cache,
        NullLogger<ActivatePeriodHandler>.Instance);

    private static AddMembershipHandler NewAdd(StudentsTestScope s) => new(
        s.ActivityGroups, s.Memberships, s.Students,
        Mock.Of<IStudentEnrollmentRepository>(), Mock.Of<IActivePeriodProvider>(),
        s.Periods, s.Cache, s.Tenants, NullLogger<AddMembershipHandler>.Instance);

    private static RolloverActivityGroupHandler NewRollover(StudentsTestScope s) => new(
        s.ActivityGroups, s.Memberships, s.Tenants, s.Periods, s.Cache,
        NullLogger<RolloverActivityGroupHandler>.Instance);

    private static async Task<(Guid yearId, Guid termId)> SeedYearAndTermAsync(StudentsTestScope s)
    {
        var create = NewCreatePeriod(s);
        var yearId = await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31)));
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        var termId = await create.HandleAsync(new CreatePeriod(
            "T1", D(2026, 9, 1), D(2026, 12, 31), PeriodType.Term, ParentPeriodId: yearId));
        await NewActivate(s).HandleAsync(new ActivatePeriod(termId));
        return (yearId, termId);
    }

    private static async Task<(Guid yearId, Guid semesterId)> SeedYearAndSemesterAsync(StudentsTestScope s)
    {
        var create = NewCreatePeriod(s, "Semesters");
        var yearId = await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31)));
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        var semesterId = await create.HandleAsync(new CreatePeriod(
            "S1", D(2026, 9, 1), D(2027, 1, 31), PeriodType.Semester, ParentPeriodId: yearId));
        await NewActivate(s).HandleAsync(new ActivatePeriod(semesterId));
        return (yearId, semesterId);
    }

    private static async Task<Guid> SeedStudentAsync(StudentsTestScope s, string number)
    {
        var student = Student.Create(number, "A", "B",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)), Guid.NewGuid()).WithTenant(s.Tenants);
        s.Db.Students.Add(student);
        await s.Db.SaveChangesAsync();
        return student.Id;
    }

    // FR-43: WholeAcademicYear membership resolves the active AcademicYear period.
    [TestMethod]
    public async Task Add_WholeAcademicYear_AttachesToActiveYear()
    {
        using var s = new StudentsTestScope("pasp-ay-" + Guid.NewGuid());
        var (yearId, _) = await SeedYearAndTermAsync(s);
        var group = ActivityGroup.Create("Year Club", span: EnrollmentSpan.WholeAcademicYear);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        var sid = await SeedStudentAsync(s, "P001");

        var id = await NewAdd(s).HandleAsync(new AddMembership(group.Id, sid));
        (await s.Memberships.GetAsync(id))!.PeriodId.Should().Be(yearId);
    }

    // FR-43: Termly membership resolves the active Term period.
    [TestMethod]
    public async Task Add_Termly_AttachesToActiveTerm()
    {
        using var s = new StudentsTestScope("pasp-term-" + Guid.NewGuid());
        var (_, termId) = await SeedYearAndTermAsync(s);
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        var sid = await SeedStudentAsync(s, "P002");

        var id = await NewAdd(s).HandleAsync(new AddMembership(group.Id, sid));
        (await s.Memberships.GetAsync(id))!.PeriodId.Should().Be(termId);
    }

    // FR-43: a provided PeriodId of the wrong type is rejected.
    [TestMethod]
    public async Task Add_Termly_WithAcademicYearPeriod_Throws()
    {
        using var s = new StudentsTestScope("pasp-wrong-" + Guid.NewGuid());
        var (yearId, _) = await SeedYearAndTermAsync(s);
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        var sid = await SeedStudentAsync(s, "P003");

        await FluentActions.Awaiting(() => NewAdd(s).HandleAsync(new AddMembership(group.Id, sid, PeriodId: yearId)))
            .Should().ThrowAsync<EnrollmentSpanMismatchException>();
    }

    // FR-45: Termly group requires a Terms framework.
    [TestMethod]
    public async Task Create_Termly_WhenDivisionNone_Throws()
    {
        using var s = new StudentsTestScope("pasp-compat-" + Guid.NewGuid());
        var h = new CreateActivityGroupHandler(s.ActivityGroups, s.Cache, s.Tenants,
            new StubAcademicYearDivisionProvider("None"), NullLogger<CreateActivityGroupHandler>.Instance);

        await FluentActions.Awaiting(() => h.HandleAsync(new CreateActivityGroup("Term Club", Span: EnrollmentSpan.Termly)))
            .Should().ThrowAsync<EnrollmentSpanIncompatibleException>();
    }

    // FR-45: Termly group allowed under a Terms framework.
    [TestMethod]
    public async Task Create_Termly_WhenDivisionTerms_Succeeds()
    {
        using var s = new StudentsTestScope("pasp-compat-ok-" + Guid.NewGuid());
        var h = new CreateActivityGroupHandler(s.ActivityGroups, s.Cache, s.Tenants,
            new StubAcademicYearDivisionProvider("Terms"), NullLogger<CreateActivityGroupHandler>.Instance);

        var id = await h.HandleAsync(new CreateActivityGroup("Term Club", Span: EnrollmentSpan.Termly));
        (await s.ActivityGroups.GetAsync(id))!.Span.Should().Be(EnrollmentSpan.Termly);
    }

    // FR-43 (H5.2): Semester membership resolves the active Semester period.
    [TestMethod]
    public async Task Add_Semester_AttachesToActiveSemester()
    {
        using var s = new StudentsTestScope("pasp-sem-" + Guid.NewGuid());
        var (_, semesterId) = await SeedYearAndSemesterAsync(s);
        var group = ActivityGroup.Create("Sem Club", span: EnrollmentSpan.Semester);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        var sid = await SeedStudentAsync(s, "P005");

        var id = await NewAdd(s).HandleAsync(new AddMembership(group.Id, sid));
        (await s.Memberships.GetAsync(id))!.PeriodId.Should().Be(semesterId);
    }

    // FR-43 (H5.2): a provided Term PeriodId of the right type + parent is accepted.
    [TestMethod]
    public async Task Add_Termly_WithProvidedTermId_Succeeds()
    {
        using var s = new StudentsTestScope("pasp-provided-" + Guid.NewGuid());
        var (_, termId) = await SeedYearAndTermAsync(s);
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        var sid = await SeedStudentAsync(s, "P006");

        var id = await NewAdd(s).HandleAsync(new AddMembership(group.Id, sid, PeriodId: termId));
        (await s.Memberships.GetAsync(id))!.PeriodId.Should().Be(termId);
    }

    // FR-43 (H5.2): a Termly membership with no active Term in the active year is rejected.
    [TestMethod]
    public async Task Add_Termly_NoActiveTerm_Throws()
    {
        using var s = new StudentsTestScope("pasp-no-term-" + Guid.NewGuid());
        var create = NewCreatePeriod(s);
        var yearId = await create.HandleAsync(new CreatePeriod("AY2026", D(2026, 9, 1), D(2027, 8, 31)));
        await NewActivate(s).HandleAsync(new ActivatePeriod(yearId));
        // No Term is created under the active year.
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        var sid = await SeedStudentAsync(s, "P007");

        await FluentActions.Awaiting(() => NewAdd(s).HandleAsync(new AddMembership(group.Id, sid)))
            .Should().ThrowAsync<EnrollmentSpanMismatchException>();
    }

    // FR-50/51: period-aligned rollover re-enrols AutoRenew members into the active term.
    [TestMethod]
    public async Task Rollover_Termly_ReenrollsIntoActiveTerm()
    {
        using var s = new StudentsTestScope("pasp-roll-" + Guid.NewGuid());
        var (_, termId) = await SeedYearAndTermAsync(s);
        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        var sid = await SeedStudentAsync(s, "P004");
        var m1 = await NewAdd(s).HandleAsync(new AddMembership(group.Id, sid));
        (await s.Memberships.GetAsync(m1))!.PeriodId.Should().Be(termId);

        await NewRollover(s).HandleAsync(new RolloverActivityGroup(group.Id, TriggerDate: D(2026, 12, 31)));

        (await s.Memberships.GetAsync(m1))!.Status.Should().Be(MembershipStatus.Exited);
        var renewed = await s.Memberships.GetActiveAsync(sid, group.Id);
        renewed.Should().NotBeNull();
        renewed!.PeriodId.Should().Be(termId, "the active term is the next window for a Termly span");
    }
}