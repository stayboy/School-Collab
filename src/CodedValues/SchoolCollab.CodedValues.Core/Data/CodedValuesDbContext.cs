using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.Domain;
using SchoolCollab.CodedValues.Core.Messaging;

namespace SchoolCollab.CodedValues.Core.Data;

public sealed class CodedValuesDbContext(DbContextOptions<CodedValuesDbContext> options)
    : DbContext(options)
{
    public DbSet<CodedValue> CodedValues => Set<CodedValue>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CodedValuesDbContext).Assembly);
    }
}
