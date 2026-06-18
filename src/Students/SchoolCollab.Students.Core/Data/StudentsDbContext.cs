using Microsoft.EntityFrameworkCore;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Messaging;

namespace SchoolCollab.Students.Core.Data;

public sealed class StudentsDbContext(DbContextOptions<StudentsDbContext> options)
    : DbContext(options)
{
    public DbSet<Student> Students => Set<Student>();
    public DbSet<GradeLevel> GradeLevels => Set<GradeLevel>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Period> Periods => Set<Period>();
    public DbSet<StudentEnrollment> StudentEnrollments => Set<StudentEnrollment>();
    public DbSet<GradeSubjectAssignment> GradeSubjectAssignments => Set<GradeSubjectAssignment>();
    public DbSet<StudentSubjectAssignment> StudentSubjectAssignments => Set<StudentSubjectAssignment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StudentsDbContext).Assembly);
    }
}