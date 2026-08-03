using ContextMemory.Admin.UI;
using ContextMemory.Admin.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddContextMemoryAdminUi();
builder.Services.PostConfigure<AdminUiOptions>(options =>
{
    if (!string.IsNullOrWhiteSpace(options.DocsPath) && Directory.Exists(options.DocsPath))
        return;

    foreach (var candidate in new[]
             {
                 Path.Combine(builder.Environment.ContentRootPath, "docs"),
                 Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "..", "docs")),
                 Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "docs")),
             })
    {
        if (Directory.Exists(candidate))
        {
            options.DocsPath = candidate;
            break;
        }
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(AdminUiAssemblyMarker).Assembly);

app.Run();
