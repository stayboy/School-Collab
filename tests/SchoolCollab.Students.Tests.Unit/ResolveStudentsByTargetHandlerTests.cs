using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.Students.Queries.ResolveStudentsByTarget;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// R2 (documents/specs/assignment-authoring-compartments.md §7.2 TGT-3…TGT-9, EC-4; D-5/D-6) —
/// the Students side of <c>IAssignmentTargetResolver</c>. Discriminating against the pre-R2 base
/// (<c>565e48f8</c>): the query does not exist there (and the <c>allStudents</c> leg, the
/// grade-agnostic stream leg and the union/dedupe are all new behaviour).
/// </summary>
[TestClass]
public class ResolveStudentsByTargetHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GradeA = Guid.Parse("22222222-2222-2222-2222-22222222222a");
    private static readonly Guid GradeB = Guid.Parse("22222222-2222-2222-2222-22222222222b");
    private static readonly Guid StreamId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GenderMale = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static Student NewStudent(StudentsTestScope s, string number, string first, string last)
    {
        var student = Student.Create(number, first, last, new DateOnly(2015, 1, 1), GenderMale)
            .WithTenant(Tenant);
        s.Db.Students.Add(student);
        return student;
    }

    private static (Student student, StudentEnrollment enrollment) Enroll(
        StudentsTestScope s, Guid periodId, Guid gradeId, Guid? streamId, string number)
    {
        var student = NewStudent(s, number, "A", number);
        var enrollment = StudentEnrollment.Create(student.Id, periodId, gradeId, streamCodedValueId: streamId)
            .WithTenant(Tenant);
        s.Db.StudentEnrollments.Add(enrollment);
        return (student, enrollment);
    }

    private static Period CurrentPeriod(StudentsTestScope s)
    {
        var period = Period.Create("2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), AcademicYearDivision.Terms);
        s.Db.Periods.Add(period);
        return period;
    }

    private static ResolveStudentsByTargetHandler Handler(StudentsTestScope s) => new(s.Db);

    private static ResolveStudentsByTarget Query(
        bool all = false, Guid[]? grades = null, Guid[]? streams = null, Guid[]? students = null, Guid[]? groups = null) =>
        new(all, grades ?? [], streams ?? [], students ?? [], groups ?? []);

    [TestMethod]
    public async Task AllStudentsLeg_ReturnsEveryNonDeletedStudent_TenantWide()
    {
        using var s = new StudentsTestScope("r2-all-" + Guid.NewGuid());
        var current = CurrentPeriod(s);
        var (kept, _) = Enroll(s, current.Id, GradeA, null, "S1");
        var (other, _) = Enroll(s, current.Id, GradeB, StreamId, "S2");
        var deleted = NewStudent(s, "S3", "Gone", "Student");
        deleted.Delete();
        await s.Db.SaveChangesAsync();

        var result = await Handler(s).HandleAsync(Query(all: true));

        result.Should().BeEquivalentTo(new[] { kept.Id, other.Id },
            "D-6: the allStudents leg is tenant-wide and excludes soft-deleted students only");
    }

    [TestMethod]
    public async Task GradeAndStreamLegs_UseTheCurrentPeriod_AndStreamsAreGradeAgnostic()
    {
        using var s = new StudentsTestScope("r2-legs-" + Guid.NewGuid());
        var current = CurrentPeriod(s);
        var (inGradeA, _) = Enroll(s, current.Id, GradeA, null, "S1");
        var (inGradeB, _) = Enroll(s, current.Id, GradeB, StreamId, "S2");

        var old = Period.Create("2020", new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31), AcademicYearDivision.Terms);
        s.Db.Periods.Add(old);
        Enroll(s, old.Id, GradeA, null, "S3");
        await s.Db.SaveChangesAsync();

        var byGrade = await Handler(s).HandleAsync(Query(grades: [GradeA]));
        byGrade.Should().BeEquivalentTo(new[] { inGradeA.Id },
            "TGT-4: the grade leg is the current-period active enrollment");

        var byStream = await Handler(s).HandleAsync(Query(streams: [StreamId]));
        byStream.Should().BeEquivalentTo(new[] { inGradeB.Id },
            "TGT-5: the stream leg carries NO grade predicate and is current-period scoped");
    }

    [TestMethod]
    public async Task UnionDedupes_AndTheGroupLegExcludesArchivedGroupsAndNonActiveMemberships()
    {
        using var s = new StudentsTestScope("r2-union-" + Guid.NewGuid());
        var current = CurrentPeriod(s);
        var (student, _) = Enroll(s, current.Id, GradeA, null, "S1");

        var activeGroup = ActivityGroup.Create("Chess").WithTenant(Tenant);
        var archivedGroup = ActivityGroup.Create("Old Club").WithTenant(Tenant);
        archivedGroup.Deactivate();
        s.Db.ActivityGroups.AddRange(activeGroup, archivedGroup);

        s.Db.ActivityGroupMemberships.Add(
            ActivityGroupMembership.Create(activeGroup.Id, student.Id).WithTenant(Tenant));
        var exited = ActivityGroupMembership.Create(activeGroup.Id, student.Id).WithTenant(Tenant);
        exited.Exit(new DateOnly(2026, 1, 2));
        s.Db.ActivityGroupMemberships.Add(exited);
        s.Db.ActivityGroupMemberships.Add(
            ActivityGroupMembership.Create(archivedGroup.Id, student.Id).WithTenant(Tenant));
        await s.Db.SaveChangesAsync();

        var union = await Handler(s).HandleAsync(
            Query(grades: [GradeA], groups: [activeGroup.Id, archivedGroup.Id], students: [student.Id]));

        union.Should().BeEquivalentTo(new[] { student.Id },
            "TGT-3: a student matching several legs appears once; EC-4 excludes the archived group");

        var archivedOnly = await Handler(s).HandleAsync(Query(groups: [archivedGroup.Id]));
        archivedOnly.Should().BeEmpty("EC-4: an archived group resolves to nobody");
    }

    [TestMethod]
    public async Task StudentLeg_ExcludesSoftDeleted_AndNoCurrentPeriodYieldsNoPeriodScopedMatches()
    {
        using var s = new StudentsTestScope("r2-student-" + Guid.NewGuid());
        // No period covers today: a past-only period is seeded deliberately, so the
        // period-scoped legs must resolve nobody (the ListStudentsByGrade posture).
        var past = Period.Create("2019", new DateOnly(2019, 1, 1), new DateOnly(2019, 12, 31), AcademicYearDivision.Terms);
        s.Db.Periods.Add(past);
        var (kept, _) = Enroll(s, past.Id, GradeA, null, "S1");
        var deleted = NewStudent(s, "S2", "Gone", "Student");
        deleted.Delete();
        await s.Db.SaveChangesAsync();

        var byStudent = await Handler(s).HandleAsync(Query(students: [kept.Id, deleted.Id]));
        byStudent.Should().BeEquivalentTo(new[] { kept.Id }, "TGT-6: soft-deleted students never match");

        var noPeriod = await Handler(s).HandleAsync(Query(grades: [GradeA]));
        noPeriod.Should().BeEmpty("no current period ⇒ the period-scoped legs match nothing");

        var explicitStudent = await Handler(s).HandleAsync(Query(students: [kept.Id]));
        explicitStudent.Should().BeEquivalentTo(new[] { kept.Id });
    }
}
