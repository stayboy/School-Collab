using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.Data.Configurations;

internal sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("students");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.StudentNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.FirstName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.LastName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.DateOfBirth);

        builder.Property(x => x.GenderCodedValueId);

        builder.Property(x => x.ContactEmail)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.ContactPhone)
            .HasMaxLength(50);

        builder.Property(x => x.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion();

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.StudentNumber)
            .IsUnique()
            .HasDatabaseName("ix_students_student_number");

        builder.HasIndex(x => x.GenderCodedValueId)
            .HasDatabaseName("ix_students_gender_cv_id");

        builder.HasIndex(x => x.IsDeleted)
            .HasDatabaseName("ix_students_is_deleted");

        builder.Ignore(x => x.DomainEvents);
    }
}