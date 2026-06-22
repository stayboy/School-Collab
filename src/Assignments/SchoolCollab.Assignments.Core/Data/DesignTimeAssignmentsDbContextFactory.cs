using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Assignments.Core.Data;

public sealed class DesignTimeAssignmentsDbContextFactory : IDesignTimeDbContextFactory<AssignmentsDbContext>
{
    public AssignmentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AssignmentsDbContext>()
            .UseNpgsql("Host=localhost;Database=schoolcollab_assignments_design")
            .UseSnakeCaseNamingConvention()
            .Options;

        var tenantProvider = new DesignTimeTenantProvider();
        return new AssignmentsDbContext(options, tenantProvider);
    }
}
