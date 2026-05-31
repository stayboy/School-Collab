using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SchoolCollab.CodedValues.Core.Data;

public sealed class DesignTimeCodedValuesDbContextFactory : IDesignTimeDbContextFactory<CodedValuesDbContext>
{
    public CodedValuesDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CodedValuesDbContext>();
        optionsBuilder
            .UseNpgsql("Host=localhost;Port=5432;Database=schoolcollab_coded_values;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention();

        return new CodedValuesDbContext(optionsBuilder.Options);
    }
}
