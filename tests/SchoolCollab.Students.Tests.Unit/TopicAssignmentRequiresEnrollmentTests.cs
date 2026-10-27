using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListActivityGroupTopicAssignments;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicAssignments;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// The <c>RequiresEnrollment</c> flag on the TPH bridge base
/// (subject-enrollment-requirement-flag.md §2 / round 13 layer 1).
/// <para>Acceptance criterion 1: the mutator no-ops when unchanged and bumps
/// <c>UpdatedAt</c> when it changes.</para>
/// <para>Acceptance criterion 2 (FR-ER-2): the two factories stamp the opposite defaults —
/// <c>false</c> for a grade, <c>true</c> for an activity group — in code, because TPH cannot
/// express a discriminator-dependent schema default.</para>
/// <para>Acceptance criterion 5, second half: the two feed readers project the value
/// <b>off the entity</b>. A hard-coded <c>false</c> in the projection compiles and passes a
/// structural grep, so each reader is asserted in both directions.</para>
/// </summary>
[TestClass]
public class TopicAssignmentRequiresEnrollmentTests
{
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static GradeTopicAssignment NewGradeAssignment() =>
        GradeTopicAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), Today());

    private static ActivityGroupTopicAssignment NewGroupAssignment() =>
        ActivityGroupTopicAssignment.Create(Guid.NewGuid(), Guid.NewGuid(), Today());

    [TestMethod]
    public void SetRequiresEnrollment_NoOpsWhenUnchanged_AndDoesNotBumpUpdatedAt()
    {
        var assignment = NewGradeAssignment();
        var updatedAt = assignment.UpdatedAt;

        assignment.SetRequiresEnrollment(false);

        assignment.RequiresEnrollment.Should().BeFalse();
        assignment.UpdatedAt.Should().Be(updatedAt,
            "the mutator no-ops on the value it already carries — no audit stamp for a non-change");
    }

    [TestMethod]
    public async Task SetRequiresEnrollment_ChangesTheValue_AndBumpsUpdatedAt()
    {
        var assignment = NewGradeAssignment();
        var updatedAt = assignment.UpdatedAt;

        await Task.Delay(20); // DateTimeOffset.UtcNow resolution
        assignment.SetRequiresEnrollment(true);

        assignment.RequiresEnrollment.Should().BeTrue();
        assignment.UpdatedAt.Should().BeAfter(updatedAt);
    }

    [TestMethod]
    public void Factories_StampOppositeDefaults_GradeFalse_GroupTrue()
    {
        NewGradeAssignment().RequiresEnrollment.Should().BeFalse(
            "a grade subject enrols every member by implication (FR-ER-1/2)");
        NewGroupAssignment().RequiresEnrollment.Should().BeTrue(
            "an activity-group subject requires explicit enrolment — the safe default (§11: stored, not yet read)");
    }

    [TestMethod]
    public async Task GradeFeedReader_ProjectsTheFlagFromTheEntity()
    {
        using var s = new StudentsTestScope("requires-enrollment-grade-feed");
        var gradeLevelId = Guid.NewGuid();
        var falseTopicId = Guid.NewGuid();
        var trueTopicId = Guid.NewGuid();

        var implied = GradeTopicAssignment.Create(gradeLevelId, falseTopicId, Today());
        var explicitEnrolment = GradeTopicAssignment.Create(gradeLevelId, trueTopicId, Today());
        explicitEnrolment.SetRequiresEnrollment(true);
        s.Db.GradeTopicAssignments.AddRange(implied, explicitEnrolment);
        await s.Db.SaveChangesAsync();

        var rows = await new ListGradeTopicAssignmentsHandler(s.Db, s.Cache)
            .HandleAsync(new ListGradeTopicAssignments(gradeLevelId, Today()));

        rows.Should().HaveCount(2);
        rows.Single(x => x.TopicId == falseTopicId).RequiresEnrollment.Should().BeFalse();
        rows.Single(x => x.TopicId == trueTopicId).RequiresEnrollment.Should().BeTrue(
            "a hard-coded false in the projection would fail here");
    }

    [TestMethod]
    public async Task ActivityGroupFeedReader_ProjectsTheFlagFromTheEntity()
    {
        using var s = new StudentsTestScope("requires-enrollment-group-feed");
        var activityGroupId = Guid.NewGuid();
        var trueTopicId = Guid.NewGuid();
        var falseTopicId = Guid.NewGuid();

        var explicitEnrolment = ActivityGroupTopicAssignment.Create(activityGroupId, trueTopicId, Today());
        var switchedBack = ActivityGroupTopicAssignment.Create(activityGroupId, falseTopicId, Today());
        switchedBack.SetRequiresEnrollment(false);
        s.Db.ActivityGroupTopicAssignments.AddRange(explicitEnrolment, switchedBack);
        await s.Db.SaveChangesAsync();

        var rows = await new ListActivityGroupTopicAssignmentsHandler(s.Db, s.Cache)
            .HandleAsync(new ListActivityGroupTopicAssignments(activityGroupId, Today()));

        rows.Should().HaveCount(2);
        rows.Single(x => x.TopicId == trueTopicId).RequiresEnrollment.Should().BeTrue();
        rows.Single(x => x.TopicId == falseTopicId).RequiresEnrollment.Should().BeFalse(
            "a hard-coded true (or a copy of the sibling's value) would fail here");
    }
}
