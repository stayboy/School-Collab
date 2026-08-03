using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit.Tenancy;

/// <summary>
/// Step 4 (global-tenant-filter.md §12): verifies that <see cref="Assignment"/>
/// is a strict tenant entity and that its owned types
/// (<see cref="AssignmentQuestion"/>, <see cref="AssignmentReview"/> and nested
/// <c>QuestionOption</c>) inherit the parent's tenant scoping — they have no
/// independent <c>DbSet</c>, so they are only reachable through a tenant-filtered
/// <see cref="AssignmentsDbContext.Assignments"/> query with <c>Include</c>.
/// </summary>
/// <remarks>
/// AC: a query as tenant A with <c>Include(Questions)</c> returns only tenant A's
/// assignment and its questions; tenant B's assignment and its questions are never
/// surfaced. Owned children cannot be queried directly (no <c>DbSet</c>), so there
/// is no unfiltered surface that could leak another tenant's owned rows.
/// </remarks>
[TestClass]
public class AssignmentOwnedTypeTenancyTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static async Task<ServiceProvider> BuildProviderAsync(string dbName)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(opts =>
            opts.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();

        // Ensure the in-memory store is created before any tenant-scoped query.
        using (var bootScope = provider.CreateScope())
        {
            var bootCtx = bootScope.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            await bootCtx.Database.EnsureCreatedAsync();
        }
        return provider;
    }

    private static void AsTenant(ServiceProvider provider, Guid tenantId)
    {
        var tenants = provider.GetRequiredService<ITenantProvider>();
        ((TenantProvider)tenants).SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));
    }

    private static Assignment NewAssignment(string title) =>
        Assignment.Create(title, null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId);

    [TestMethod]
    public async Task OwnedQuestions_InheritParentTenant_NoCrossTenantLeak()
    {
        using var provider = await BuildProviderAsync("asg-owned-tenancy");
        var tenants = provider.GetRequiredService<ITenantProvider>();

        // Tenant A: one assignment with two questions.
        AsTenant(provider, TenantA);
        using (var scopeA = provider.CreateScope())
        {
            var dbA = scopeA.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            var a = NewAssignment("A-Assignment");
            a.AddQuestion("A-Q1", QuestionType.MultipleChoice, 1);
            a.AddQuestion("A-Q2", QuestionType.MultipleChoice, 2);
            dbA.Assignments.Add(a);
            await dbA.SaveChangesAsync();
        }

        // Tenant B: one assignment with one question.
        AsTenant(provider, TenantB);
        using (var scopeB = provider.CreateScope())
        {
            var dbB = scopeB.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            var b = NewAssignment("B-Assignment");
            b.AddQuestion("B-Q1", QuestionType.MultipleChoice, 1);
            dbB.Assignments.Add(b);
            await dbB.SaveChangesAsync();
        }

        // Query as tenant A with Include(Questions) → only A's assignment + A's 2 questions.
        AsTenant(provider, TenantA);
        using (var scopeQ = provider.CreateScope())
        {
            var db = scopeQ.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            var results = await db.Assignments
                .Include(a => a.Questions)
                .ToListAsync();

            results.Should().HaveCount(1, "tenant A sees only its own assignment");
            results[0].Title.Should().Be("A-Assignment");
            results[0].Questions.Should().HaveCount(2, "A's owned questions load via Include");
            results[0].Questions.Should().OnlyContain(q => q.QuestionText.StartsWith("A-"),
                "no tenant B questions leak through the owned collection");
        }

        // Query as tenant B with Include(Questions) → only B's assignment + B's 1 question.
        AsTenant(provider, TenantB);
        using (var scopeQ = provider.CreateScope())
        {
            var db = scopeQ.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            var results = await db.Assignments
                .Include(a => a.Questions)
                .ToListAsync();

            results.Should().HaveCount(1, "tenant B sees only its own assignment");
            results[0].Title.Should().Be("B-Assignment");
            results[0].Questions.Should().HaveCount(1);
        }

        // Total cross-tenant verification: 2 assignments exist, each with its own questions,
        // and every assignment is stamped with its owner's tenant.
        using (var scopeAll = provider.CreateScope())
        {
            var db = scopeAll.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            var all = await db.Assignments
                .Include(a => a.Questions)
                .IgnoreQueryFilters(["Tenant"])
                .ToListAsync();

            all.Should().HaveCount(2);
            all.Single(a => a.Title == "A-Assignment").TenantId.Should().Be(TenantA);
            all.Single(a => a.Title == "B-Assignment").TenantId.Should().Be(TenantB);
            all.Sum(a => a.Questions.Count).Should().Be(3, "2 (A) + 1 (B)");
        }
    }

    [TestMethod]
    public async Task OwnedReviews_InheritParentTenant_NoCrossTenantLeak()
    {
        using var provider = await BuildProviderAsync("asg-owned-reviews");

        // Tenant A: assignment with a review.
        AsTenant(provider, TenantA);
        using (var scopeA = provider.CreateScope())
        {
            var dbA = scopeA.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            var a = NewAssignment("A-Reviewed");
            a.AddReview(TeacherId, 95m, "Good");
            dbA.Assignments.Add(a);
            await dbA.SaveChangesAsync();
        }

        // Tenant B: assignment with a review.
        AsTenant(provider, TenantB);
        using (var scopeB = provider.CreateScope())
        {
            var dbB = scopeB.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            var b = NewAssignment("B-Reviewed");
            b.AddReview(TeacherId, 80m, "Ok");
            dbB.Assignments.Add(b);
            await dbB.SaveChangesAsync();
        }

        // Tenant A sees only its own review.
        AsTenant(provider, TenantA);
        using (var scopeQ = provider.CreateScope())
        {
            var db = scopeQ.ServiceProvider.GetRequiredService<AssignmentsDbContext>();
            var results = await db.Assignments.Include(a => a.Reviews).ToListAsync();
            results.Should().HaveCount(1);
            results[0].Reviews.Should().HaveCount(1);
            results[0].Reviews[0].Score.Should().Be(95m);
        }
    }
}
