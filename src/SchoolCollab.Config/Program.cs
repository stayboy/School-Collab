using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Core.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IFeatureFlagService, FeatureFlagService>();

var app = builder.Build();

app.MapGet("/api/features", (IFeatureFlagService featureService) => 
    Results.Ok(featureService.GetAllFlags()));

app.Run();
