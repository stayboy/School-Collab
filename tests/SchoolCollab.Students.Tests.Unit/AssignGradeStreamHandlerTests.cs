using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.GradeStreams.Commands.AssignGradeStream;
using SchoolCollab.Students.Core.CQRS.GradeStreams.Commands.RemoveGradeStream;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// AC3 (assignment + stream-version uniqueness + idempotence + the empty-version
/// escape hatch), AC10 (cross-tenant isolation over a real in-memory
/// <see cref="StudentsDbContext"/>) and the remove half of the bridge.
/// Pre-fix there is no bridge, no assign handler and no
/// <see cref="DuplicateStreamAssignmentException"/> at all.
/// </summary>
/// <remarks>
/// The scope is built by each TEST METHOD, never inside a helper: the tenant
/// provider keeps its tenant in an <see cref="System.Threading.AsyncLocal{T}"/>,
/// and a scope constructed in a called async method does not reliably carry that
/// tenant back to the caller (the save-guard then rejects the strict write).
/// </remarks>
[TestClass]
public class AssignGradeStreamHandlerTests
{
    private static readonly Guid OtherTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static async Task<Guid> SeedGradeAsync(StudentsTestScope scope)
    {
        var grade = GradeLevel.Create(Guid.NewGuid(), level: 5, name: "Grade 5", displayOrder: 5);
        scope.Db.GradeLevels.Add(grade);
        await scope.Db.SaveChangesAsync();
        return grade.Id;
    }

    private static AssignGradeStreamHandler NewHandler(StudentsTestScope s, StubCodedValuesApi api) =>
        new(s.GradeStreamAssignments, s.GradeLevels, api, s.Cache, NullLogger<AssignGradeStreamHandler>.Instance);

    [TestMethod]
    public async Task FirstAssign_Succeeds_AndLinksTheStream()
    {
        using var s = new StudentsTestScope("ags-first");
        var gradeLevelId = await SeedGradeAsync(s);
        var streamId = Guid.NewGuid();
        var api = new StubCodedValuesApi()
            .WithStream(StreamDtoFactory.Stream(streamId, "5A", "Grade 5 - A", version: "5A"));

        var assignmentId = await NewHandler(s, api).HandleAsync(new AssignGradeStream(gradeLevelId, streamId));

        assignmentId.Should().NotBeEmpty();
        (await s.GradeStreamAssignments.ExistsAsync(gradeLevelId, streamId)).Should().BeTrue();
    }

    [TestMethod]
    public async Task DuplicateStreamVersion_Throws_AndInsertsNothing()
    {
        using var s = new StudentsTestScope("ags-dup");
        var gradeLevelId = await SeedGradeAsync(s);
        var existingStreamId = Guid.NewGuid();
        var incomingStreamId = Guid.NewGuid();
        await s.GradeStreamAssignments.AddOrReuseAsync(
            GradeStreamAssignment.Create(gradeLevelId, existingStreamId));

        var api = new StubCodedValuesApi()
            .WithStream(StreamDtoFactory.Stream(existingStreamId, "5A", "Grade 5 - A", version: "5A"))
            .WithStream(StreamDtoFactory.Stream(incomingStreamId, "5X", "Another 5A", version: "5A"));
        api.Catalogue = [api.ById[existingStreamId]];

        var act = () => NewHandler(s, api).HandleAsync(new AssignGradeStream(gradeLevelId, incomingStreamId));

        var ex = (await act.Should().ThrowAsync<DuplicateStreamAssignmentException>()).Which;
        ex.GradeLevelId.Should().Be(gradeLevelId);
        ex.StreamVersion.Should().Be("5A");
        ex.ExistingCodedValueId.Should().Be(existingStreamId);
        (await s.GradeStreamAssignments.ExistsAsync(gradeLevelId, incomingStreamId)).Should().BeFalse(
            "the rejected assignment must not be persisted");
    }

    [TestMethod]
    public async Task DifferentStreamVersion_IsAllowed()
    {
        using var s = new StudentsTestScope("ags-other-version");
        var gradeLevelId = await SeedGradeAsync(s);
        var existingStreamId = Guid.NewGuid();
        var incomingStreamId = Guid.NewGuid();
        await s.GradeStreamAssignments.AddOrReuseAsync(
            GradeStreamAssignment.Create(gradeLevelId, existingStreamId));

        var api = new StubCodedValuesApi()
            .WithStream(StreamDtoFactory.Stream(existingStreamId, "5A", "Grade 5 - A", version: "5A"))
            .WithStream(StreamDtoFactory.Stream(incomingStreamId, "5B", "Grade 5 - B", version: "5B"));
        api.Catalogue = [api.ById[existingStreamId]];

        await NewHandler(s, api).HandleAsync(new AssignGradeStream(gradeLevelId, incomingStreamId));

        (await s.GradeStreamAssignments.ExistsAsync(gradeLevelId, incomingStreamId)).Should().BeTrue();
    }

    [TestMethod]
    public async Task StreamWithNoVersion_NeverTripsTheUniquenessRule()
    {
        using var s = new StudentsTestScope("ags-no-version");
        var gradeLevelId = await SeedGradeAsync(s);
        var existingStreamId = Guid.NewGuid();
        var incomingStreamId = Guid.NewGuid();
        await s.GradeStreamAssignments.AddOrReuseAsync(
            GradeStreamAssignment.Create(gradeLevelId, existingStreamId));

        // Neither stream carries a streamVersion label — a brand-new user-created
        // stream has none until the label is set.
        var api = new StubCodedValuesApi()
            .WithStream(StreamDtoFactory.Stream(existingStreamId, "5A", "Grade 5 - A"))
            .WithStream(StreamDtoFactory.Stream(incomingStreamId, "5B", "Grade 5 - B"));
        api.Catalogue = [api.ById[existingStreamId]];

        await NewHandler(s, api).HandleAsync(new AssignGradeStream(gradeLevelId, incomingStreamId));

        (await s.GradeStreamAssignments.ExistsAsync(gradeLevelId, incomingStreamId)).Should().BeTrue();
    }

    [TestMethod]
    public async Task ReAssign_ReturnsTheExistingBridgeId()
    {
        using var s = new StudentsTestScope("ags-idempotent");
        var gradeLevelId = await SeedGradeAsync(s);
        var streamId = Guid.NewGuid();
        var api = new StubCodedValuesApi()
            .WithStream(StreamDtoFactory.Stream(streamId, "5A", "Grade 5 - A", version: "5A"));

        var first = await NewHandler(s, api).HandleAsync(new AssignGradeStream(gradeLevelId, streamId));
        var second = await NewHandler(s, api).HandleAsync(new AssignGradeStream(gradeLevelId, streamId));

        second.Should().Be(first, "the unique index makes a re-assign idempotent");
        (await s.Db.GradeStreamAssignments.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task UnknownGradeLevel_Throws_NotFound()
    {
        using var s = new StudentsTestScope("ags-unknown-grade");
        var streamId = Guid.NewGuid();
        var api = new StubCodedValuesApi()
            .WithStream(StreamDtoFactory.Stream(streamId, "5A", "Grade 5 - A"));

        var act = () => NewHandler(s, api).HandleAsync(new AssignGradeStream(Guid.NewGuid(), streamId));

        await act.Should().ThrowAsync<GradeLevelNotFoundException>();
    }

    [TestMethod]
    public async Task UnknownStreamCodedValue_ThrowsMismatch()
    {
        using var s = new StudentsTestScope("ags-unknown-stream");
        var gradeLevelId = await SeedGradeAsync(s);

        var act = () => NewHandler(s, new StubCodedValuesApi())
            .HandleAsync(new AssignGradeStream(gradeLevelId, Guid.NewGuid()));

        await act.Should().ThrowAsync<StreamGradeMismatchException>();
    }

    // ── AC10: cross-tenant isolation ────────────────────────────────────────

    [TestMethod]
    public async Task ExistsAsync_ForAnotherTenantsGradeAndStream_ReturnsFalse()
    {
        // The plan pins this suite to a real/in-memory DbContext + hand-rolled
        // repository so the strict tenant filter is genuinely exercised (a mocked
        // repository would pass this vacuously).
        using var s = new StudentsTestScope("ags-cross-tenant");
        var gradeLevelId = await SeedGradeAsync(s);
        var streamId = Guid.NewGuid();

        using (s.TenantAccessor.SuppressTenantGuard())
        {
            s.Db.GradeStreamAssignments.Add(
                GradeStreamAssignment.Create(gradeLevelId, streamId).WithTenant(OtherTenantId));
            await s.Db.SaveChangesAsync();
        }

        (await s.GradeStreamAssignments.ExistsAsync(gradeLevelId, streamId)).Should().BeFalse(
            "another tenant's (grade, stream) pair is invisible under the strict tenant filter");
        (await s.GradeStreamAssignments.ListByGradeLevelAsync(gradeLevelId)).Should().BeEmpty();
    }

    // ── Remove ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Remove_DeletesTheBridgeRow_AndLeavesTheCodedValueAlone()
    {
        using var s = new StudentsTestScope("ags-remove");
        var gradeLevelId = await SeedGradeAsync(s);
        var streamId = Guid.NewGuid();
        var assignmentId = (await s.GradeStreamAssignments.AddOrReuseAsync(
            GradeStreamAssignment.Create(gradeLevelId, streamId))).Id;

        await new RemoveGradeStreamHandler(
                s.GradeStreamAssignments, s.Cache, NullLogger<RemoveGradeStreamHandler>.Instance)
            .HandleAsync(new RemoveGradeStream(gradeLevelId, assignmentId));

        (await s.GradeStreamAssignments.ExistsAsync(gradeLevelId, streamId)).Should().BeFalse();
    }

    [TestMethod]
    public async Task Remove_ForAMismatchedGrade_Throws()
    {
        using var s = new StudentsTestScope("ags-remove-mismatch");
        var gradeLevelId = await SeedGradeAsync(s);
        var assignmentId = (await s.GradeStreamAssignments.AddOrReuseAsync(
            GradeStreamAssignment.Create(gradeLevelId, Guid.NewGuid()))).Id;

        var act = () => new RemoveGradeStreamHandler(
                s.GradeStreamAssignments, s.Cache, NullLogger<RemoveGradeStreamHandler>.Instance)
            .HandleAsync(new RemoveGradeStream(Guid.NewGuid(), assignmentId));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
