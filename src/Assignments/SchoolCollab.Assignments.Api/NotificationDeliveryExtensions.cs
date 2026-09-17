using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Assignments.Core.Services.Delivery;

namespace SchoolCollab.Assignments.Api;

/// <summary>
/// DI registration for the WS-E2 (ar-16) notification delivery drain and its
/// cross-context address resolver. The <c>Add{Layer}()</c> rule forbids inline
/// <c>services.Add*()</c> in <c>Program.cs</c>, so the call site is
/// <c>builder.Services.AddNotificationDispatchSweep()</c> in
/// <see cref="SchoolCollab.Assignments.Api.Program"/> (the same seam as
/// <c>AddAssignmentLifecycleSweeps</c>).
/// </summary>
public static class NotificationDeliveryExtensions
{
    /// <summary>Registers the notification drain hosted service + the Students-API address resolver.</summary>
    public static IServiceCollection AddNotificationDispatchSweep(this IServiceCollection services)
    {
        services.AddScoped<IContactAddressResolver, StudentsContactAddressResolver>();
        services.AddHostedService<NotificationDispatchSweepService>();
        return services;
    }
}
