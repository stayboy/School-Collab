using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.GradeStreams.Queries.ListGradeStreams;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// AC2 — the Streams-card read path. The discriminator is the OVERRIDE-AWARE name:
/// the base card resolved its rows through the attribute-filtered Settings read,
/// so a tenant override came back resolved; the bridge read must too (via the
/// override-resolving <c>by-parent</c> hop, filtered to the bridge's stream ids).
/// AC10's cross-tenant assertions run over the real in-memory
/// <see cref="StudentsDbContext"/> so the strict tenant filter genuinely applies.
/// </summary>
/// <remarks>
/// The scope is built by each TEST METHOD, never inside a helper: the tenant
/// provider keeps its tenant in an <see cref="System.Threading.AsyncLocal{T}"/>,
/// and a scope constructed in a called async method does not reliably carry that
/// tenant back to the caller (the save-guard then rejects the strict write).
/// </remarks>
[TestClass]
public class ListGradeStreamsHandlerTests
{
    private static readonly Guid OtherTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static ListGradeStreamsHandler NewHandler(StudentsTestScope s, StubCodedValuesApi api) =>
        new(s.GradeStreamAssignments, api, NullLogger<ListGradeStreamsHandler>.Instance);

    private static async Task<Guid> SeedGradeAsync(StudentsTestScope scope)
    {
        var grade = GradeLevel.Create(Guid.NewGuid(), level: 5, name: "Grade 5", displayOrder: 5);
        scope.Db.GradeLevels.Add(grade);
        await scope.Db.SaveChangesAsync();
        return grade.Id;
    }

    [TestMethod]
    public async Task NoBridgeRows_ReturnsEmpty_WithoutReadingTheCatalogue()
    {
        using var s = new StudentsTestScope("lgs-empty");
        var gradeLevelId = await SeedGradeAsync(s);
        var api = new StubCodedValuesApi();

        var result = await NewHandler(s, api).HandleAsync(new ListGradeStreams(gradeLevelId));

        result.Should().BeEmpty();
        api.ByParentCalls.Should().BeEmpty("an empty grade needs no catalogue hop");
    }

    [TestMethod]
    public async Task ReturnsOverrideAwareName_AndIsOverriddenFlag()
    {
        using var s = new StudentsTestScope("lgs-override");
        var gradeLevelId = await SeedGradeAsync(s);
        var streamId = Guid.NewGuid();
        var assignment = await s.GradeStreamAssignments.AddOrReuseAsync(
            GradeStreamAssignment.Create(gradeLevelId, streamId));

        // The resolved DTO the override-resolving by-parent endpoint returns: Name is
        // the tenant-resolved name, DefaultName the global blueprint name.
        var api = new StubCodedValuesApi
        {
            Catalogue =
            [
                StreamDtoFactory.Stream(
                    streamId, "5A", "Standard 5A", version: "5A",
                    isOverridden: true, defaultName: "Grade 5 - A"),
            ],
        };

        var result = await NewHandler(s, api).HandleAsync(new ListGradeStreams(gradeLevelId));

        var dto = result.Should().ContainSingle().Which;
        dto.AssignmentId.Should().Be(assignment.Id);
        dto.StreamCodedValueId.Should().Be(streamId);
        dto.GradeLevelId.Should().Be(gradeLevelId);
        dto.Name.Should().Be("Standard 5A", "Name is the override-aware resolved name");
        dto.NameOverride.Should().Be("Grade 5 - A", "the override metadata is carried, not lost");
        dto.IsOverridden.Should().BeTrue();
        dto.StreamVersion.Should().Be("5A");
        api.ByParentCalls.Should().Equal(GradeStreamAssignment.CatalogueParentCode);
    }

    [TestMethod]
    public async Task GlobalStream_ReportsNoOverride()
    {
        using var s = new StudentsTestScope("lgs-global");
        var gradeLevelId = await SeedGradeAsync(s);
        var streamId = Guid.NewGuid();
        await s.GradeStreamAssignments.AddOrReuseAsync(GradeStreamAssignment.Create(gradeLevelId, streamId));

        var api = new StubCodedValuesApi
        {
            Catalogue = [StreamDtoFactory.Stream(streamId, "5A", "Grade 5 - A", version: "5A")],
        };

        var dto = (await NewHandler(s, api).HandleAsync(new ListGradeStreams(gradeLevelId)))
            .Should().ContainSingle().Which;

        dto.IsOverridden.Should().BeFalse();
        dto.NameOverride.Should().BeNull();
        dto.Name.Should().Be("Grade 5 - A");
    }

    [TestMethod]
    public async Task BridgeRowMissingFromTheCatalogue_IsSkipped()
    {
        using var s = new StudentsTestScope("lgs-missing");
        var gradeLevelId = await SeedGradeAsync(s);
        await s.GradeStreamAssignments.AddOrReuseAsync(GradeStreamAssignment.Create(gradeLevelId, Guid.NewGuid()));

        var api = new StubCodedValuesApi { Catalogue = [] };

        var result = await NewHandler(s, api).HandleAsync(new ListGradeStreams(gradeLevelId));

        result.Should().BeEmpty("a deleted coded value must not render as a blank row");
    }

    // ── AC10: cross-tenant isolation ────────────────────────────────────────

    [TestMethod]
    public async Task AnotherTenantsBridgeRow_IsNotReturned()
    {
        using var s = new StudentsTestScope("lgs-cross-tenant");
        var gradeLevelId = await SeedGradeAsync(s);
        var foreignStreamId = Guid.NewGuid();

        using (s.TenantAccessor.SuppressTenantGuard())
        {
            s.Db.GradeStreamAssignments.Add(
                GradeStreamAssignment.Create(gradeLevelId, foreignStreamId).WithTenant(OtherTenantId));
            await s.Db.SaveChangesAsync();
        }

        var api = new StubCodedValuesApi
        {
            Catalogue = [StreamDtoFactory.Stream(foreignStreamId, "5A", "Grade 5 - A")],
        };

        var result = await NewHandler(s, api).HandleAsync(new ListGradeStreams(gradeLevelId));

        result.Should().BeEmpty(
            "the strict tenant filter means a tenant never sees another tenant's offers");
        api.ByParentCalls.Should().BeEmpty("no bridge rows for this tenant means no catalogue hop");
    }
}
