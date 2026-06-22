using Microsoft.EntityFrameworkCore;
using SchoolCollab.CodedValues.Core.Data;
using SchoolCollab.Core.Identity;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.CodedValues.Api.Infrastructure.Data;

/// <summary>
/// Legacy seeding class. Seeding for CodedValues (including SuperAdmin) is now
/// expected to run via the unified MigrationService. This type is retained only
/// to avoid breaking references and will be removed once all callers are updated.
/// </summary>
public static class DbInitializer
{
    public static Task Initialize(IServiceProvider serviceProvider)
    {
        // No-op: seeding now runs in SchoolCollab.MigrationService.
        // This method is kept for backward compatibility and tests that may
        // still call it directly; update those tests before removing.
        return Task.CompletedTask;
    }
}
