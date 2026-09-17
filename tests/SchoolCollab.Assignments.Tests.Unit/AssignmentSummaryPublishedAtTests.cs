using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-E2b / ar-17 (pre-PR review finding: missing layer coverage) — the
/// failure-surface gate is <c>PublishedAt is not null</c>, and that value has to
/// survive three layers (entity → Core <c>AssignmentSummary</c> → contract
/// <c>AssignmentSummaryDto</c>).
/// <para>
/// A layer that forgets to project it does <em>not</em> fail to compile — the
/// record parameter is optional — it silently reports "never published", and the
/// ar-17 notification-failures tab disappears. These tests pin the projection at
/// each repository read so that silent-drop class is caught here instead of by a
/// UI assertion, and they also pin the premise the whole gate rests on:
/// <see cref="Assignment.Publish"/> stamps <c>PublishedAt</c>.
/// </para>
/// </summary>
[TestClass]
public class AssignmentSummaryPublishedAtTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid WardStudentId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid ContactId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    private static (AssignmentsDbContext db, AssignmentRepository repo, IWardAssignmentProjectionRepository wardRepo) Build(string database)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(database));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        var tenants = (TenantProvider)provider.GetRequiredService<ITenantProvider>();
        tenants.SetTenant(new TenantContext(TenantA, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();

        return (db, new AssignmentRepository(db), new WardAssignmentProjectionRepository(db));
    }

    private static Assignment NewAssignment(Guid tenantId) =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId)
            .WithTenant(tenantId);

    [TestMethod]
    public async Task ListAsync_ProjectsPublishedAt_AndReportsNullWhenNeverPublished()
    {
        var (db, repo, _) = Build(nameof(ListAsync_ProjectsPublishedAt_AndReportsNullWhenNeverPublished));
        using var _db = db;

        var draft = NewAssignment(TenantA);

        var published = NewAssignment(TenantA);
        published.Publish(approvalRequired: false);
        // The gate rests on Publish() stamping this — assert the premise, not just the plumbing.
        published.PublishedAt.Should().NotBeNull("Assignment.Publish must stamp PublishedAt");
        var publishedAt = published.PublishedAt!.Value;

        db.Assignments.AddRange(draft, published);
        await db.SaveChangesAsync();

        var summaries = await repo.ListAsync(null);

        summaries.Single(s => s.Id == published.Id).PublishedAt.Should().Be(publishedAt,
            "AssignmentRepository.ListAsync must project PublishedAt — an unprojected read reports every " +
            "published assignment as never-published, which silently hides the ar-17 failure surface");

        summaries.Single(s => s.Id == draft.Id).PublishedAt.Should().BeNull(
            "a never-published assignment reports null, which is what keeps its (empty) failures tab hidden");
    }

    [TestMethod]
    public async Task ListWardAssignmentsAsync_ProjectsPublishedAt()
    {
        var (db, _, wardRepo) = Build(nameof(ListWardAssignmentsAsync_ProjectsPublishedAt));
        using var _db = db;

        var published = NewAssignment(TenantA);
        published.Publish(approvalRequired: false);
        var publishedAt = published.PublishedAt!.Value;

        db.Assignments.Add(published);
        db.AssignmentRecipients.Add(AssignmentRecipient.Create(
            TenantA, published.Id, ContactOwnerType.Guardian, WardStudentId, WardStudentId,
            ContactId, ContactChannel.Email, role: null,
            notifyOnBroadcast: true, subscriptionActive: true));
        await db.SaveChangesAsync();

        var rows = await wardRepo.ListWardAssignmentsAsync(WardStudentId, DateTimeOffset.UtcNow);

        rows.Should().ContainSingle();
        rows[0].PublishedAt.Should().Be(publishedAt,
            "WardAssignmentProjectionRepository is a SECOND construction site of the same summary DTO and " +
            "must project PublishedAt too — it was initially missed, and because the parameter is optional " +
            "the omission compiled silently");
    }
}
