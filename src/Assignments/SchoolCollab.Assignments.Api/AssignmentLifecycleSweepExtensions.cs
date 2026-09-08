using SchoolCollab.Assignments.Api.Services;

namespace SchoolCollab.Assignments.Api;

/// <summary>DI registration for the assignment lifecycle sweeps
/// (WS-A2 / spec §3.5 step 8 / §7 Q6). The
/// <see cref="Add{Layer}()"/> rule forbids inline
/// <c>services.Add*()</c> in <c>Program.cs</c>, so the call site is
/// <c>builder.Services.AddAssignmentLifecycleSweeps()</c> in
/// <see cref="SchoolCollab.Assignments.Api.Program"/>.</summary>
public static class AssignmentLifecycleSweepExtensions
{
    public static IServiceCollection AddAssignmentLifecycleSweeps(this IServiceCollection services)
    {
        services.AddHostedService<ScheduledPublishSweepService>();
        services.AddHostedService<ArchiveSweepService>();
        return services;
    }
}
