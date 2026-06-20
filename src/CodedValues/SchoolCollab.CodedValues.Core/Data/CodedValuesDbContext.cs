using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Messaging;
using SchoolCollab.Core.Identity;

namespace SchoolCollab.CodedValues.Core.Data;

public sealed class CodedValuesDbContext(DbContextOptions<CodedValuesDbContext> options)
    : DbContext(options)
{
    public DbSet<CodedValue> CodedValues => Set<CodedValue>();
    public DbSet<TenantCodedValueOverride> TenantCodedValueOverrides => Set<TenantCodedValueOverride>();
    public DbSet<TenantCodedValueAttributeOverride> TenantCodedValueAttributeOverrides => Set<TenantCodedValueAttributeOverride>();
    public DbSet<User> Users => Set<User>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CodedValuesDbContext).Assembly);

        modelBuilder.Entity<CodedValue>().HasQueryFilter(cv => !cv.IsDeleted);
    }
}
