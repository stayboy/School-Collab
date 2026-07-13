using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
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
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<GuardianNameHistory> GuardianNameHistories => Set<GuardianNameHistory>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactSubscription> ContactSubscriptions => Set<ContactSubscription>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<TeacherSubject> TeacherSubjects => Set<TeacherSubject>();
    public DbSet<TeacherGradeLevel> TeacherGradeLevels => Set<TeacherGradeLevel>();
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
        modelBuilder.ApplyConfiguration(new GradeLevelConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new SubjectConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new PeriodConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new StudentEnrollmentConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new GuardianConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new GuardianNameHistoryConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new StudentGuardianConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new ContactConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new ContactSubscriptionConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new TeacherConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new TeacherSubjectConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new TeacherGradeLevelConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new GradeSubjectAssignmentConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new StudentSubjectAssignmentConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new SubjectStrandConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new SubjectLessonConfiguration(() => CurrentTenantId));
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(OutboxMapping.FlagsFor<StudentsDbContext>()));

        // FR-18 / AC-17: build-time model audit — every non-allow-listed, non-owned
        // entity MUST have a "Tenant" named query filter.
        ValidateTenantFilters(modelBuilder);
    }

    /// <summary>
    /// Global entities in this context (no tenant filter). <see cref="OutboxMessage"/>
    /// is the queue table (TenantId is dispatch-routing payload, FR-15). Every other
    /// Students entity is strict tenant-scoped (§3.2).
    /// </summary>
    protected override Type[] GlobalEntityAllowList => [typeof(OutboxMessage)];
}
