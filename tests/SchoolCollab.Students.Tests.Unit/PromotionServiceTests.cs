using FluentAssertions;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class PromotionServiceTests
{
    [TestMethod]
    public void StudentsPromotedEvent_IsDomainEvent()
    {
        var evt = new StudentsPromotedEvent(Guid.NewGuid(), Guid.NewGuid(), 42);

        evt.Should().BeAssignableTo<IDomainEvent>();
        evt.StudentCount.Should().Be(42);
    }

    [TestMethod]
    public void StudentsPromotedEvent_StoresPeriodIds()
    {
        var fromId = Guid.NewGuid();
        var toId = Guid.NewGuid();

        var evt = new StudentsPromotedEvent(fromId, toId, 10);

        evt.FromPeriodId.Should().Be(fromId);
        evt.ToPeriodId.Should().Be(toId);
        evt.StudentCount.Should().Be(10);
    }

    [TestMethod]
    public void Period_Complete_WhenActive_SetsStatusToCompleted()
    {
        var period = Period.Create("Test Period", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        period.Activate();

        period.Complete();

        period.Status.Should().Be(PeriodStatus.Completed);
    }

    [TestMethod]
    public void Period_Complete_WhenDraft_Throws()
    {
        var period = Period.Create("Test Period", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

        var act = () => period.Complete();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Only active periods can be completed.");
    }

    [TestMethod]
    public void Period_SetNextPeriod_SetsNextPeriodId()
    {
        var period = Period.Create("Semester 1", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        var nextPeriodId = Guid.NewGuid();

        period.SetNextPeriod(nextPeriodId);

        period.NextPeriodId.Should().Be(nextPeriodId);
    }

    [TestMethod]
    public void StudentEnrollment_Create_SetsActiveStatus()
    {
        var studentId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var gradeLevelId = Guid.NewGuid();

        var enrollment = StudentEnrollment.Create(studentId, periodId, gradeLevelId);

        enrollment.Status.Should().Be(EnrollmentStatus.Active);
        enrollment.StudentId.Should().Be(studentId);
        enrollment.PeriodId.Should().Be(periodId);
        enrollment.GradeLevelId.Should().Be(gradeLevelId);
    }

    [TestMethod]
    public void StudentEnrollment_Transfer_SetsTransferredStatus()
    {
        var enrollment = StudentEnrollment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var newGradeLevelId = Guid.NewGuid();

        enrollment.Transfer(newGradeLevelId);

        enrollment.Status.Should().Be(EnrollmentStatus.Transferred);
        enrollment.GradeLevelId.Should().Be(newGradeLevelId);
        enrollment.ExitDate.Should().NotBeNull();
    }

    [TestMethod]
    public void StudentEnrollment_Transfer_WhenWithdrawn_Throws()
    {
        var enrollment = StudentEnrollment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        enrollment.Withdraw();

        var act = () => enrollment.Transfer(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Only active enrollments can be transferred.");
    }

    [TestMethod]
    public void StudentEnrollment_Withdraw_SetsWithdrawnStatus()
    {
        var enrollment = StudentEnrollment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        enrollment.Withdraw();

        enrollment.Status.Should().Be(EnrollmentStatus.Withdrawn);
        enrollment.ExitDate.Should().NotBeNull();
    }
}