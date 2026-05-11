using ModelContextProtocol.AspNetCore;
using OpenApiToMcp.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Read endpoints from appsettings.json "OpenApiToMcp" section
//    Each endpoint becomes an independent MCP service at /mcp/{endpoint.Name}
builder.Services.AddOpenApiMcp(builder.Configuration);

// 2. Add MCP server with multi-endpoint tool resolution
builder.Services.AddMcpServer()
    .WithMultiEndpointOpenApiTools()
    .WithHttpTransport();

builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

// 3. Map MCP endpoints — {endpoint} is the route parameter matched to endpoint Name
app.MapMcp("/mcp/{endpoint}");

// 4. Health check / index
app.MapGet("/", (EndpointToolResolver resolver) =>
{
    var endpoints = resolver.EndpointNames.ToList();
    return Results.Json(new
    {
        service = "OpenApiToMcp Multi-Endpoint",
        endpoints = endpoints.Select(name => new
        {
            name,
            mcpUrl = $"/mcp/{name}"
        })
    });
});

// 5. Status per endpoint
app.MapGet("/mcp/{endpoint}/status", (string endpoint, EndpointToolResolver resolver) =>
{
    if (resolver.TryGetTools(endpoint, out var tools))
        return Results.Json(new { endpoint, toolCount = tools.Count, status = "ok" });
    return Results.NotFound(new { endpoint, error = "Endpoint not found" });
});

app.Run();
