using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Builds an in-memory <see cref="StudentsDbContext"/> + a real <see cref="HybridCache"/>
/// sharing one <see cref="ITenantProvider"/> (the default System/Empty tenant), so
/// write handlers, read handlers, and the DbContext all agree on the tenant. The
/// internal repositories are reachable because Students.Core grants
/// <c>InternalsVisibleTo</c> to this assembly.
/// </summary>
internal sealed class StudentsTestScope : IDisposable
{
    public StudentsDbContext Db { get; }
    public HybridCache Cache { get; }
    public ITenantProvider Tenants { get; }
    public ITenantContextAccessor TenantAccessor { get; }
    public PeriodRepository Periods { get; }
    public GradeLevelRepository GradeLevels { get; }
    public TopicRepository Topics { get; }
    public GradeTopicAssignmentRepository GradeTopicAssignments { get; }
    public ActivityGroupRepository ActivityGroups { get; }
    public ActivityGroupMembershipRepository Memberships { get; }
    public StudentRepository Students { get; }

    /// <summary>The bridge repository over the in-memory context — the enroll/transfer
    /// suites validate streams against actual <c>grade_stream_assignments</c> rows.</summary>
    public IGradeStreamAssignmentRepository GradeStreamAssignments { get; }

    public StudentsTestScope(string name)
        : this(name, interceptor: null)
    {
    }

    /// <summary>
    /// As the default scope, with an optional <see cref="SaveChangesInterceptor"/> — the seam for
    /// asserting write atomicity (how many <c>SaveChanges</c> calls a compound write makes) and
    /// for forcing a save to fail so a rollback can be asserted.
    /// </summary>
    public StudentsTestScope(string name, SaveChangesInterceptor? interceptor)
    {
        _name = name;
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<StudentsDbContext>(o =>
        {
            o.UseInMemoryDatabase(name);
            if (interceptor is not null) o.AddInterceptors(interceptor);
        });
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        var sp = services.BuildServiceProvider();

        Db = sp.GetRequiredService<StudentsDbContext>();
        Cache = sp.GetRequiredService<HybridCache>();
        // Same singleton the DbContext resolved, so SetTenant (if used) is visible
        // to the context's query filters.
        Tenants = sp.GetRequiredService<ITenantProvider>();
        // The sanctioned bypass for test setup that writes cross-tenant data.
        TenantAccessor = sp.GetRequiredService<ITenantContextAccessor>();
        // Set a real tenant so the save-guard (FR-5) allows strict-entity creation.
        // The default Guid.Empty context is rejected by the guard.
        ((TenantProvider)Tenants).SetTenant(new TenantContext(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "TestSchool",
            TenantType.School));
        Periods = new PeriodRepository(Db);
        GradeLevels = new GradeLevelRepository(Db);
        Topics = new TopicRepository(Db);
        GradeTopicAssignments = new GradeTopicAssignmentRepository(Db);
        ActivityGroups = new ActivityGroupRepository(Db);
        Memberships = new ActivityGroupMembershipRepository(Db);
        Students = new StudentRepository(Db);
        GradeStreamAssignments = new InMemoryGradeStreamAssignmentRepository(Db);
    }

    public void Dispose() => Db.Dispose();

    private readonly string _name;

    /// <summary>
    /// A fresh context over the <b>same</b> in-memory database and the same tenant provider, with no
    /// interceptor — so a test can read what was actually persisted after a failed save (the
    /// original context still tracks the unsaved changes and would answer from them).
    /// </summary>
    public StudentsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<StudentsDbContext>()
            .UseInMemoryDatabase(_name)
            .Options,
            Tenants);

    /// <summary>
    /// Builds an <see cref="IConfiguration"/> that sets the global period activation-window
    /// tolerance (<c>Students:PeriodActivationToleranceDays</c>). Tests that exercise the
    /// window guard pass a small value; tests that only exercise hierarchy/guard behaviour
    /// pass a large value so the window guard never interferes with their scenarios.
    /// </summary>
    public static IConfiguration Config(int toleranceDays) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Students:PeriodActivationToleranceDays"] = toleranceDays.ToString()
            })
            .Build();
}