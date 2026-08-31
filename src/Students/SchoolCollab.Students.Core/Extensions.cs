using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Settings.Contracts.Events;
using SchoolCollab.Students.Core.Projections;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain.Specifications;
using SchoolCollab.Students.Core.Tenancy;
using SchoolCollab.Students.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Students.Core;

public static class Extensions
{
    public static IServiceCollection AddStudentsCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Ensure the tenant provider is available for the DbContext and handlers
        // even when this module is used without authentication (e.g. worker/tests).
        services.AddTenancy();
        var connectionString = configuration.GetConnectionString("students-db")
            ?? configuration["ConnectionStrings:students-db"]
            ?? "Host=localhost;Port=5432;Database=schoolcollab_students;Username=postgres;Password=postgres";

        services.AddDbContextFactory<StudentsDbContext>(opts =>
            opts.UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention());

        // Request-scoped unit of work for atomic compound commands (e.g.
        // create-teacher-with-assignments). Wraps a batch of writes in one
        // EF Core transaction committed once at the end.
        services.AddScoped<IUnitOfWork<StudentsDbContext>, UnitOfWork<StudentsDbContext>>();

        services.AddScoped<IStudentRepository, StudentRepository>();
        services.AddScoped<IGradeLevelRepository, GradeLevelRepository>();

        // Local coded-value read model (adr-cross-module-calls.md Phase 1):
        // projection repository + event handlers that keep it current.
        services.AddScoped<ILocalCodedValueRepository, LocalCodedValueRepository>();
        services.AddScoped<IIntegrationEventHandler<CodedValueCreated>, CodedValueCreatedProjectionHandler>();
        services.AddScoped<IIntegrationEventHandler<CodedValueUpdated>, CodedValueUpdatedProjectionHandler>();
        services.AddScoped<IIntegrationEventHandler<CodedValueDisabled>, CodedValueDisabledProjectionHandler>();
        services.AddScoped<IIntegrationEventHandler<CodedValueEnabled>, CodedValueEnabledProjectionHandler>();
        services.AddScoped<IIntegrationEventHandler<CodedValueDeleted>, CodedValueDeletedProjectionHandler>();
        services.AddScoped<IIntegrationEventHandler<CodedValueOverrideUpserted>, CodedValueOverrideUpsertedProjectionHandler>();
        services.AddScoped<IIntegrationEventHandler<CodedValueOverrideRemoved>, CodedValueOverrideRemovedProjectionHandler>();
        services.AddScoped<ITopicRepository, TopicRepository>();
        services.AddScoped<IPeriodRepository, PeriodRepository>();
        services.AddScoped<IActivePeriodProvider, ActivePeriodProvider>();
        services.AddScoped<IStudentEnrollmentRepository, StudentEnrollmentRepository>();
        services.AddScoped<IGradeTopicAssignmentRepository, GradeTopicAssignmentRepository>();
        services.AddScoped<IActivityGroupTopicAssignmentRepository, ActivityGroupTopicAssignmentRepository>();
        services.AddScoped<ITopicAssignmentRepository, TopicAssignmentRepository>();
        services.AddScoped<IStudentTopicAssignmentRepository, StudentTopicAssignmentRepository>();
        services.AddScoped<IGuardianRepository, GuardianRepository>();
        services.AddScoped<IContactRepository, ContactRepository>();
        services.AddScoped<ITeacherRepository, TeacherRepository>();

        // Audit: contact change log (who/why for edit/delete). Scoped so it can
        // resolve the request-scoped IActorAccessor while writing into the same
        // DbContext transaction as the mutation handler.
        services.AddScoped<ContactAuditor>();
        services.AddScoped<IActivityGroupRepository, ActivityGroupRepository>();
        services.AddScoped<IActivityGroupMembershipRepository, ActivityGroupMembershipRepository>();

        // Enrollment validation specifications (plan §3). The three leaf rules are
        // registered as ILeafEnrollmentSpecification so the composite receives them via
        // IEnumerable<ILeafEnrollmentSpecification>; the marker interface keeps the composite
        // (registered as ICompositeEnrollmentSpecification) out of its own
        // dependency set, avoiding a circular resolution. The handler depends on
        // ICompositeEnrollmentSpecification (the gateway) and maps the failing
        // rule to its typed exception — no concrete spec is injected into the
        // handler, and each spec is instantiated once per scope.
        services.AddScoped<ILeafEnrollmentSpecification, AgeRangeSpecification>();
        services.AddScoped<ILeafEnrollmentSpecification, GenderRestrictionSpecification>();
        services.AddScoped<ILeafEnrollmentSpecification, SingleActiveEnrollmentSpecification>();
        services.AddScoped<ICompositeEnrollmentSpecification, CompositeEnrollmentSpecification>();

        // Default audit actor; the API host overrides this with ClaimsPrincipalActorAccessor.
        services.AddSingleton<IActorAccessor>(_ => new SystemActorAccessor("system:students", "Students System"));

        // Notification & Delivery: shared effective-policy resolver.
        services.AddSingleton<IEffectiveNotificationPolicyResolver, EffectiveNotificationPolicyResolver>();

        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            };
        });

        var assembly = typeof(Extensions).Assembly;
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithTransientLifetime());
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithTransientLifetime());
        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithTransientLifetime());

        services.AddOutbox<StudentsDbContext>(configuration);

        return services;
    }
}