using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Commands.UpsertGradeAssignmentPolicy;
using SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Queries.GetGradeAssignmentPolicy;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Round A (<c>documents/solution/assignment-policy-fields.md</c> §4) — the Students CQRS pair
/// round-trips the whole override shape, keeps the grade-exists guard, and keeps 204-when-no-row
/// (all fields inherit) unchanged. Round <c>assignment-rules-policy-rework</c> (D3/D5, AC7) extends
/// that shape with the guardian-review flag and the archive window.
/// </summary>
[TestClass]
public class GradeAssignmentPolicyHandlerTests
{
    private static async Task<Guid> SeedGradeAsync(StudentsTestScope s, string name = "Grade 1")
    {
        var gl = GradeLevel.Create(Guid.NewGuid(), 1, name, 1);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    private static GetGradeAssignmentPolicyHandler NewGet(StudentsTestScope s) =>
        new(s.Db);

    private static UpsertGradeAssignmentPolicyHandler NewUpsert(StudentsTestScope s) =>
        new(s.Db, s.Tenants);

    [TestMethod]
    public async Task Get_WhenNoRow_ReturnsNull()
    {
        using var s = new StudentsTestScope("gap-get-null");
        var gradeId = await SeedGradeAsync(s);

        var result = await NewGet(s).HandleAsync(new GetGradeAssignmentPolicy(gradeId));
        result.Should().BeNull("no override row means the grade inherits the tenant default");
    }

    [TestMethod]
    public async Task Upsert_CreatesOverride_GetRoundTripsEveryField()
    {
        using var s = new StudentsTestScope("gap-upsert-create");
        var gradeId = await SeedGradeAsync(s);

        await NewUpsert(s).HandleAsync(new UpsertGradeAssignmentPolicy(
            gradeId, SignatureRequirementMode.Optional, RequiresApprovalBeforePublish: true,
            MaxPrimaryContacts: 2, MaxCopyContacts: 4,
            MandatoryReview: true, ArchiveGraceDays: 45));

        var result = await NewGet(s).HandleAsync(new GetGradeAssignmentPolicy(gradeId));

        result.Should().NotBeNull();
        result!.GradeLevelId.Should().Be(gradeId);
        result.SignatureRequirement.Should().Be(SignatureRequirementMode.Optional);
        result.RequiresApprovalBeforePublish.Should().BeTrue();
        result.MaxPrimaryContacts.Should().Be(2);
        result.MaxCopyContacts.Should().Be(4);
        result.MandatoryReview.Should().BeTrue("the grade's guardian-review override round-trips (D3)");
        result.ArchiveGraceDays.Should().Be(45, "the grade's archive-window override round-trips (D5)");
    }

    [TestMethod]
    public async Task Upsert_ReplacesWithInherit()
    {
        using var s = new StudentsTestScope("gap-upsert-inherit");
        var gradeId = await SeedGradeAsync(s);
        var upsert = NewUpsert(s);

        await upsert.HandleAsync(new UpsertGradeAssignmentPolicy(
            gradeId, SignatureRequirementMode.Mandatory, true, 1, 1,
            MandatoryReview: true, ArchiveGraceDays: 45));
        await upsert.HandleAsync(new UpsertGradeAssignmentPolicy(gradeId, null, null, null, null, null, null));

        var result = await NewGet(s).HandleAsync(new GetGradeAssignmentPolicy(gradeId));

        result!.SignatureRequirement.Should().BeNull(); // inherit restored
        result.RequiresApprovalBeforePublish.Should().BeNull();
        result.MaxPrimaryContacts.Should().BeNull();
        result.MaxCopyContacts.Should().BeNull();
        result.MandatoryReview.Should().BeNull("a null field restores 'inherit the tenant default' (D3)");
        result.ArchiveGraceDays.Should().BeNull();
        (await s.Db.GradeAssignmentPolicies.CountAsync()).Should().Be(1, "one override row per grade");
    }

    [TestMethod]
    public async Task Upsert_PartialFieldSet_LeavesTheRestInheriting()
    {
        using var s = new StudentsTestScope("gap-upsert-partial");
        var gradeId = await SeedGradeAsync(s);

        await NewUpsert(s).HandleAsync(new UpsertGradeAssignmentPolicy(
            gradeId, null, null, MaxPrimaryContacts: 6, null));

        var result = await NewGet(s).HandleAsync(new GetGradeAssignmentPolicy(gradeId));

        result!.MaxPrimaryContacts.Should().Be(6);
        result.MaxCopyContacts.Should().BeNull();
        result.SignatureRequirement.Should().BeNull();
        result.RequiresApprovalBeforePublish.Should().BeNull();
    }

    [TestMethod]
    public async Task Upsert_UnknownGrade_ThrowsGradeLevelNotFound()
    {
        using var s = new StudentsTestScope("gap-upsert-nograde");
        var act = () => NewUpsert(s).HandleAsync(
            new UpsertGradeAssignmentPolicy(Guid.NewGuid(), SignatureRequirementMode.Optional, null, null, null));
        await act.Should().ThrowAsync<GradeLevelNotFoundException>();
    }

    // ── Q7 — non-positive contact caps are rejected on the write path ─────

    [TestMethod]
    public async Task Upsert_WithZeroPrimaryCap_ThrowsAndPersistsNothing()
    {
        using var s = new StudentsTestScope("gap-upsert-zero-primary");
        var gradeId = await SeedGradeAsync(s);

        var act = () => NewUpsert(s).HandleAsync(new UpsertGradeAssignmentPolicy(
            gradeId, SignatureRequirementMode.Optional, null, MaxPrimaryContacts: 0, MaxCopyContacts: 3));

        var ex = await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("MaxPrimaryContacts", "the guard names the offending field");
        (await s.Db.GradeAssignmentPolicies.CountAsync()).Should().Be(0,
            "a rejected cap never creates an override row");
    }

    [TestMethod]
    public async Task Upsert_WithNegativeCopyCap_ThrowsAndPersistsNothing()
    {
        using var s = new StudentsTestScope("gap-upsert-negative-copy");
        var gradeId = await SeedGradeAsync(s);

        var act = () => NewUpsert(s).HandleAsync(new UpsertGradeAssignmentPolicy(
            gradeId, SignatureRequirementMode.Optional, null, MaxPrimaryContacts: 3, MaxCopyContacts: -2));

        var ex = await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        ex.Which.ParamName.Should().Be("MaxCopyContacts", "the guard names the offending field");
        (await s.Db.GradeAssignmentPolicies.CountAsync()).Should().Be(0,
            "the guard runs before the row is created");
    }

    [TestMethod]
    public async Task Upsert_WithNullCaps_IsAccepted()
    {
        using var s = new StudentsTestScope("gap-upsert-null-caps");
        var gradeId = await SeedGradeAsync(s);

        var result = await NewUpsert(s).HandleAsync(new UpsertGradeAssignmentPolicy(
            gradeId, SignatureRequirementMode.Mandatory, null, MaxPrimaryContacts: null, MaxCopyContacts: null));

        result.MaxPrimaryContacts.Should().BeNull("null stays inherit — it is not a non-positive cap");
        result.MaxCopyContacts.Should().BeNull();
        result.SignatureRequirement.Should().Be(SignatureRequirementMode.Mandatory);
    }
}
