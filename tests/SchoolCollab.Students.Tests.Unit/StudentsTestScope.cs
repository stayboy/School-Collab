using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
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
    public PeriodRepository Periods { get; }
    public GradeLevelRepository GradeLevels { get; }
    public SubjectRepository Subjects { get; }
    public GradeSubjectAssignmentRepository GradeSubjectAssignments { get; }

    public StudentsTestScope(string name)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<StudentsDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        var sp = services.BuildServiceProvider();

        Db = sp.GetRequiredService<StudentsDbContext>();
        Cache = sp.GetRequiredService<HybridCache>();
        // Same singleton the DbContext resolved, so SetTenant (if used) is visible
        // to the context's query filters.
        Tenants = sp.GetRequiredService<ITenantProvider>();
        Periods = new PeriodRepository(Db);
        GradeLevels = new GradeLevelRepository(Db);
        Subjects = new SubjectRepository(Db);
        GradeSubjectAssignments = new GradeSubjectAssignmentRepository(Db);
    }

    public void Dispose() => Db.Dispose();
}