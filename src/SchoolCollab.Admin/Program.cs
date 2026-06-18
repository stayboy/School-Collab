using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Components;
using SchoolCollab.Assignments.Admin;
using SchoolCollab.CodedValues.Admin;
using SchoolCollab.Students.Admin;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();

// Register module services (HttpClient factories for each bounded context)
builder.Services.AddCodedValuesModule();
builder.Services.AddAssignmentsModule();
builder.Services.AddStudentsModule();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapDefaultEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(SchoolCollab.CodedValues.Admin.Components._Imports).Assembly,
        typeof(SchoolCollab.Assignments.Admin.Components._Imports).Assembly,
        typeof(SchoolCollab.Students.Admin.Components._Imports).Assembly);

app.Run();