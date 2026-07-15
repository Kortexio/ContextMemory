var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.ContextMemory_Api>("api")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.ContextMemory_Admin_Web>("admin-web")
    .WithReference(api)
    .WithEnvironment("ApiBaseUrl", api.GetEndpoint("http"))
    .WithExternalHttpEndpoints();

builder.Build().Run();
