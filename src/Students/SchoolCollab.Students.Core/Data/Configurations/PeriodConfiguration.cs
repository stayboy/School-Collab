using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Core.Data;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class PeriodConfiguration : EntityTypeConfigurationBase<Period>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Period> builder)
    {
        builder.ToTable("periods");

        builder.ConfigureAuditProperties();
        builder.ConfigurePostgresRowVersion();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.StartDate).IsRequired();
        builder.Property(x => x.EndDate).IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasDefaultValue(PeriodStatus.Draft);

        builder.Property(x => x.AllowSubjectOverrides)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.NextPeriodId);


        builder.HasIndex(x => x.Status)
            .HasDatabaseName("ix_periods_status");

        builder.HasIndex(x => x.StartDate)
            .HasDatabaseName("ix_periods_start_date");

        builder.Ignore(x => x.DomainEvents);
    }
}