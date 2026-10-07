using FluentAssertions;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// R2 (documents/specs/assignment-authoring-compartments.md §7.1 TGT-1/TGT-2, TGT-13, §7.4 TGT-15;
/// round decisions D-1, D-8.1) plus round <c>drop-primary-grade</c> (the D-2 primary-grade rule is
/// retired; the policy-scope grade is derived from the grade targets). Discriminating against the
/// pre-R2 base (<c>565e48f8</c>): <see cref="AssignmentTarget"/> and <see cref="Assignment.SetTargets"/>
/// do not exist there, so every assertion below fails to compile / fails at runtime on the base.
/// </summary>
[TestClass]
public class AssignmentTargetTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid GradeA = Guid.Parse("22222222-2222-2222-2222-22222222222a");
    private static readonly Guid GradeB = Guid.Parse("22222222-2222-2222-2222-22222222222b");
    private static readonly Guid StreamId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid StudentId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid GroupA = Guid.Parse("55555555-5555-5555-5555-55555555555a");
    private static readonly Guid GroupB = Guid.Parse("55555555-5555-5555-5555-55555555555b");

    private static Assignment NewAssignment() =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId)
            .WithTenant(TenantId);

    // ── TGT-2: the row's own shape ────────────────────────────────────────────

    [TestMethod]
    public void Create_AllStudents_CarriesNoRefId_AndAnyOtherKindRequiresOne()
    {
        AssignmentTarget.Create(TenantId, Guid.NewGuid(), TargetKind.AllStudents, null, 0)
            .RefId.Should().BeNull("TGT-2: AllStudents has no reference id");

        var allStudentsWithRef = () => AssignmentTarget.Create(
            TenantId, Guid.NewGuid(), TargetKind.AllStudents, Guid.NewGuid(), 0);
        allStudentsWithRef.Should().Throw<ArgumentException>();

        var gradeWithoutRef = () => AssignmentTarget.Create(
            TenantId, Guid.NewGuid(), TargetKind.GradeLevel, null, 0);
        gradeWithoutRef.Should().Throw<ArgumentException>("every non-AllStudents kind needs a ref");

        var negativeOrder = () => AssignmentTarget.Create(
            TenantId, Guid.NewGuid(), TargetKind.Student, StudentId, -1);
        negativeOrder.Should().Throw<ArgumentException>();
    }

    // ── TGT-13 / TGT-2 / TGT-1 uniqueness ────────────────────────────────────

    [TestMethod]
    public void SetTargets_EmptySet_IsRejected()
    {
        var assignment = NewAssignment();
        var act = () => assignment.SetTargets([], TenantId);
        act.Should().Throw<ArgumentException>("TGT-13: at least one target is required");
    }

    [TestMethod]
    public void SetTargets_AllStudents_IsExclusiveAndUnique()
    {
        var assignment = NewAssignment();

        var mixed = () => assignment.SetTargets(
            [(TargetKind.AllStudents, null), (TargetKind.GradeLevel, (Guid?)GradeA)], TenantId);
        mixed.Should().Throw<ArgumentException>("TGT-2: AllStudents is mutually exclusive");

        var twice = () => assignment.SetTargets(
            [(TargetKind.AllStudents, null), (TargetKind.AllStudents, null)], TenantId);
        twice.Should().Throw<ArgumentException>("one AllStudents row per assignment");

        assignment.SetTargets([(TargetKind.AllStudents, null)], TenantId);
        assignment.Targets.Should().ContainSingle().Which.Kind.Should().Be(TargetKind.AllStudents);
    }

    [TestMethod]
    public void SetTargets_DuplicateKindAndRef_IsRejected()
    {
        var assignment = NewAssignment();
        var act = () => assignment.SetTargets(
            [(TargetKind.Student, (Guid?)StudentId), (TargetKind.Student, (Guid?)StudentId)], TenantId);

        act.Should().Throw<ArgumentException>("TGT-1: (Kind, RefId) is unique per assignment");
    }

    // ── UX-21: full replacement when non-null, preserve when null ─────────────

    [TestMethod]
    public void SetTargets_NullPreserves_TheSetAndReindexesDisplayOrder()
    {
        var assignment = NewAssignment();
        assignment.SetTargets(
            [(TargetKind.GradeLevel, (Guid?)GradeA), (TargetKind.Student, (Guid?)StudentId)], TenantId);

        assignment.SetTargets(null, TenantId);
        assignment.Targets.Should().HaveCount(2, "null = preserve (the child-collection contract)");

        assignment.SetTargets([(TargetKind.Student, (Guid?)StudentId)], TenantId);
        var target = assignment.Targets.Should().ContainSingle().Subject;
        target.DisplayOrder.Should().Be(0, "DisplayOrder re-indexes 0..n-1 on every replacement");
    }

    // ── Round drop-primary-grade: no authored primary grade; two grade targets are publishable ──

    [TestMethod]
    public void SetTargets_TwoGradeTargets_NeedNoPrimaryGrade()
    {
        var assignment = NewAssignment();
        assignment.SetTargets(
            [(TargetKind.GradeLevel, (Guid?)GradeA), (TargetKind.GradeLevel, (Guid?)GradeB)], TenantId);

        assignment.Targets.Should().HaveCount(2,
            "round drop-primary-grade: the authored grade targets ARE the grade scope — the retired "
            + "D-2 guard refused exactly this set without a primary grade (AC-4)");
    }

    // ── AssignmentPolicyScope: the one policy-scope derivation rule (round drop-primary-grade) ──

    [TestMethod]
    public void DeriveGrade_Matrix()
    {
        AssignmentPolicyScope.DeriveGrade([]).Should().BeNull("no grade target ⇒ the tenant default");
        AssignmentPolicyScope.DeriveGrade([GradeA]).Should().Be(GradeA, "exactly one distinct grade ⇒ that grade");
        AssignmentPolicyScope.DeriveGrade([GradeA, GradeA]).Should().Be(GradeA, "duplicates collapse");
        AssignmentPolicyScope.DeriveGrade([GradeA, GradeB]).Should().BeNull(
            "two or more distinct grade targets ⇒ the tenant-default policy (AC-5b)");

        var assignment = NewAssignment();
        assignment.SetTargets(
            [(TargetKind.GradeLevel, (Guid?)GradeB), (TargetKind.Stream, (Guid?)StreamId)], TenantId);

        AssignmentPolicyScope.DeriveGrade(assignment).Should().Be(GradeB, "only grade-target rows count");
    }

    // ── D-8.1 / FR-22: the archived-group rule ───────────────────────────────

    [TestMethod]
    public void SetTargets_NewlyAddedArchivedGroup_IsRejected_ButAPersistedOneReSaves()
    {
        var assignment = NewAssignment();
        assignment.SetTargets([(TargetKind.ActivityGroup, (Guid?)GroupA)], TenantId);

        var added = () => assignment.SetTargets(
            [(TargetKind.ActivityGroup, (Guid?)GroupA), (TargetKind.ActivityGroup, (Guid?)GroupB)],
            TenantId, [GroupB]);
        added.Should().Throw<ArgumentException>(
            "FR-22 / AC-15: a NEWLY-ADDED group target whose group is archived is rejected");

        // The already-persisted target is NOT re-validated (the historical row must survive).
        var reSave = () => assignment.SetTargets(
            [(TargetKind.ActivityGroup, (Guid?)GroupA)], TenantId, [GroupA]);
        reSave.Should().NotThrow();
        assignment.Targets.Should().ContainSingle();
    }

    // ── D-1 / TGT-15: the derived compat column matrix ───────────────────────

    [TestMethod]
    public void SyncDerivedTargeting_Matrix()
    {
        var assignment = NewAssignment();

        assignment.TargetAudienceType.Should().Be(TargetAudienceType.Mixed,
            "D-1: a set with no rows derives Mixed");

        assignment.SetTargets([(TargetKind.AllStudents, null)], TenantId);
        assignment.TargetAudienceType.Should().Be(TargetAudienceType.AllStudents);

        assignment.SetTargets([(TargetKind.GradeLevel, (Guid?)GradeA)], TenantId);
        assignment.TargetAudienceType.Should().Be(TargetAudienceType.SelectedGrades);

        assignment.SetTargets([(TargetKind.ActivityGroup, (Guid?)GroupA)], TenantId);
        assignment.TargetAudienceType.Should().Be(TargetAudienceType.SelectedGroups);

        assignment.SetTargets([(TargetKind.Stream, (Guid?)StreamId)], TenantId);
        assignment.TargetAudienceType.Should().Be(TargetAudienceType.Mixed,
            "a Stream/Student-only set matches no legacy single-choice value");
    }
}
