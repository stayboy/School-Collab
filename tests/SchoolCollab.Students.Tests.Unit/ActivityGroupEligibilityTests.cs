using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// The display mirror of the server's activity-group membership rules
/// (<see cref="ActivityGroupEligibility"/>). Every case here has a server twin in
/// <c>AddMembershipHandler</c> — <c>InactiveGroupException</c>, <c>GradeNotEligibleException</c>,
/// <c>EnrollmentWindowClosedException</c>, <c>GroupAtCapacityException</c> — so a change on either side
/// that is not mirrored shows up as a failing row.
/// </summary>
[TestClass]
public class ActivityGroupEligibilityTests
{
    private static readonly Guid Grade8 = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid Grade9 = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly DateOnly Today = new(2025, 10, 24);

    private static ActivityGroupDto Group(
        string name = "Chess Club",
        bool isActive = true,
        Guid[]? eligibleGrades = null,
        int? capacity = null,
        int activeMembers = 0,
        DateOnly? opens = null,
        DateOnly? closes = null) =>
        new(
            Id: Guid.NewGuid(),
            Name: name,
            Description: null,
            Category: null,
            Capacity: capacity,
            IsActive: isActive,
            Span: "OpenEnded",
            EnrollmentStartDate: opens,
            EnrollmentEndDate: closes,
            AutoRenewDefault: true,
            EligibleGradeIds: eligibleGrades ?? [],
            ActiveMemberCount: activeMembers,
            CreatedAt: DateTimeOffset.UnixEpoch,
            UpdatedAt: DateTimeOffset.UnixEpoch);

    [TestMethod]
    public void An_active_open_group_for_any_grade_is_addable()
    {
        var option = EvaluateSingle(Group(), grade: Grade8);

        option.IsValid.Should().BeTrue();
        option.CanAdd.Should().BeTrue();
        option.InvalidReason.Should().BeNull("there is nothing to explain when the option is available");
    }

    [TestMethod]
    public void An_empty_eligible_grade_set_means_any_grade()
    {
        // Repository contract (FR-40): empty array ⇒ every grade is eligible.
        var option = EvaluateSingle(Group(eligibleGrades: []), grade: Grade9);

        option.CanAdd.Should().BeTrue("an empty eligible-grade set is not a restriction");
    }

    [TestMethod]
    public void A_switched_off_group_is_rejected_with_a_reason()
    {
        var option = EvaluateSingle(Group(isActive: false), grade: Grade8);

        option.CanAdd.Should().BeFalse();
        option.InvalidReason.Should().Be("Group is switched off");
    }

    [TestMethod]
    public void A_group_offered_to_other_grades_is_rejected()
    {
        var option = EvaluateSingle(Group(eligibleGrades: [Grade9]), grade: Grade8);

        option.CanAdd.Should().BeFalse();
        option.InvalidReason.Should().Be("Not offered to this grade");
    }

    [TestMethod]
    public void A_group_offered_to_this_grade_is_addable()
    {
        var option = EvaluateSingle(Group(eligibleGrades: [Grade8, Grade9]), grade: Grade8);

        option.CanAdd.Should().BeTrue();
    }

    [TestMethod]
    public void A_grade_restricted_group_cannot_be_joined_without_an_active_enrollment()
    {
        // Server twin: EnsureGradeEligible only runs when the group has an eligible-grade set, and a
        // student with no active enrollment for the active period fails it.
        var restricted = EvaluateSingle(Group(eligibleGrades: [Grade8]), grade: null);

        restricted.CanAdd.Should().BeFalse();
        restricted.InvalidReason.Should().Be("Not enrolled in a grade yet");
    }

    [TestMethod]
    public void An_unrestricted_group_stays_addable_without_an_active_enrollment()
    {
        // Server twin: an empty eligible set means "any grade", so EnsureGradeEligible returns before it
        // ever looks the student's enrollment up.
        var unrestricted = EvaluateSingle(Group(), grade: null);

        unrestricted.CanAdd.Should().BeTrue("an unrestricted group has no grade to check");
        unrestricted.InvalidReason.Should().BeNull();
    }

    [TestMethod]
    public void A_closed_window_is_rejected_with_its_date()
    {
        var option = EvaluateSingle(Group(closes: Today.AddDays(-1)), grade: Grade8);

        option.CanAdd.Should().BeFalse();
        option.InvalidReason.Should().StartWith("Enrolment window closed (");
    }

    [TestMethod]
    public void A_future_window_is_rejected_with_its_opening_date()
    {
        var option = EvaluateSingle(Group(opens: Today.AddDays(3)), grade: Grade8);

        option.CanAdd.Should().BeFalse();
        option.InvalidReason.Should().StartWith("Enrolment opens ");
    }

    [TestMethod]
    public void A_window_that_includes_today_is_addable()
    {
        var option = EvaluateSingle(Group(opens: Today.AddDays(-7), closes: Today), grade: Grade8);

        option.CanAdd.Should().BeTrue("both bounds are inclusive");
    }

    [TestMethod]
    public void A_full_group_is_rejected_with_its_counts()
    {
        var option = EvaluateSingle(Group(capacity: 12, activeMembers: 12), grade: Grade8);

        option.CanAdd.Should().BeFalse();
        option.InvalidReason.Should().Be("Full (12/12)");
    }

    [TestMethod]
    public void A_group_with_room_or_no_capacity_limit_is_addable()
    {
        EvaluateSingle(Group(capacity: 12, activeMembers: 11), grade: Grade8).CanAdd.Should().BeTrue();
        EvaluateSingle(Group(capacity: null, activeMembers: 900), grade: Grade8).CanAdd.Should().BeTrue(
            "a null capacity means unlimited");
    }

    [TestMethod]
    public void A_group_the_student_already_holds_is_marked_a_member_and_not_addable()
    {
        var group = Group();

        var option = ActivityGroupEligibility
            .Evaluate([group], Grade8, [group.Id], Today)
            .Single();

        option.IsMember.Should().BeTrue();
        option.CanAdd.Should().BeFalse("joining twice is the duplicate-membership case the server rejects");
        option.InvalidReason.Should().BeNull("a membership is a fact, not an error to explain");
    }

    [TestMethod]
    public void Every_group_is_returned_in_input_order_with_one_option_each()
    {
        var groups = new[] { Group("Alpha"), Group("Beta"), Group("Gamma") };

        var options = ActivityGroupEligibility.Evaluate(groups, Grade8, [], Today);

        options.Select(o => o.Group.Name).Should().Equal("Alpha", "Beta", "Gamma");
        options.Should().HaveCount(3);
    }

    private static ActivityGroupOption EvaluateSingle(ActivityGroupDto group, Guid? grade) =>
        ActivityGroupEligibility.Evaluate([group], grade, [], Today).Single();
}
