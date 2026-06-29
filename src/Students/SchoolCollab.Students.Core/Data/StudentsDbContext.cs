using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data.Configurations;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Core.Messaging;

namespace SchoolCollab.Students.Core.Data;

public sealed class StudentsDbContext(DbContextOptions<StudentsDbContext> options, ITenantProvider tenantProvider)
    : ModuleDbContext(options, tenantProvider)
{
    public DbSet<Student> Students => Set<Student>();
    public DbSet<GradeLevel> GradeLevels => Set<GradeLevel>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Period> Periods => Set<Period>();
    public DbSet<StudentEnrollment> StudentEnrollments => Set<StudentEnrollment>();
    public DbSet<GradeSubjectAssignment> GradeSubjectAssignments => Set<GradeSubjectAssignment>();
    public DbSet<StudentSubjectAssignment> StudentSubjectAssignments => Set<StudentSubjectAssignment>();
    public DbSet<SubjectStrand> SubjectStrands => Set<SubjectStrand>();
    public DbSet<SubjectLesson> SubjectLessons => Set<SubjectLesson>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations explicitly so constructor-injected context instances are
        // available to tenant-aware configuration base classes. Do not use
        // ApplyConfigurationsFromAssembly here because it cannot inject arguments.
        modelBuilder.ApplyConfiguration(new StudentConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new GradeLevelConfiguration());
        modelBuilder.ApplyConfiguration(new SubjectConfiguration());
        modelBuilder.ApplyConfiguration(new PeriodConfiguration());
        modelBuilder.ApplyConfiguration(new StudentEnrollmentConfiguration());
        modelBuilder.ApplyConfiguration(new GradeSubjectAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new StudentSubjectAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new SubjectStrandConfiguration());
        modelBuilder.ApplyConfiguration(new SubjectLessonConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }
}
