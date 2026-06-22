using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SchoolCollab.Core.Data;

namespace SchoolCollab.Students.Core.Data;

public sealed class DesignTimeStudentsDbContextFactory : IDesignTimeDbContextFactory<StudentsDbContext>
{
    public StudentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StudentsDbContext>()
            .UseNpgsql("Host=localhost;Database=schoolcollab_students_design")
            .UseSnakeCaseNamingConvention()
            .Options;

        var tenantProvider = new DesignTimeTenantProvider();
        return new StudentsDbContext(options, tenantProvider);
    }
}
