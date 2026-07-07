using Microsoft.AspNetCore.Mvc;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.Tenants.Queries.ListTenants;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class TenantRoutes
{
    public static RouteGroupBuilder MapTenantRoutes(this RouteGroupBuilder group)
    {
        // List every tenant (global registry) for the dev tenant switcher dropdown.
        group.MapGet("/", async (
            [FromServices] IQueryHandler<ListTenants, TenantDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTenants(), ct)));

        return group;
    }
}