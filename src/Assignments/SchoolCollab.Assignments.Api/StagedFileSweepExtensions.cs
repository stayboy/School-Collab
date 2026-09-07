using SchoolCollab.Assignments.Api.Services;

namespace SchoolCollab.Assignments.Api;

/// <summary>
/// DI registration for the staged-file orphan sweep (WS-A1 / decision
/// (b)). Lives in the Api project (the sanctioned pre-worker hosted-service
/// seam — no Assignments worker exists yet). The
/// <see cref="Add{Layer}()"/> rule forbids inline <c>services.Add*()</c>
/// in <c>Program.cs</c>, so the call site is <c>builder.Services.AddStagedFileSweep()</c>
/// in <see cref="SchoolCollab.Assignments.Api.Program"/>.
/// </summary>
public static class StagedFileSweepExtensions
{
    public static IServiceCollection AddStagedFileSweep(this IServiceCollection services)
    {
        services.AddHostedService<StagedFileSweepService>();
        return services;
    }
}
