# OpenApiToMcp

Convert standard OpenAPI specifications into MCP (Model Context Protocol) services callable by LLMs.

## Packages

| Package | Description |
|---------|-------------|
| **OpenApiToMcp.Core** | Core library: OpenAPI parsing, overlay support, operation mapping, schema conversion, API execution |
| **OpenApiToMcp.AspNetCore** | ASP.NET Core integration: `AddOpenApiMcp()` and `WithOpenApiTools()` extension methods |
| **OpenApiToMcp.Cli** | CLI tool: `openapi-to-mcp --spec <path>` to run an MCP server from an OpenAPI spec |

## Quick Start

### Single Endpoint (ASP.NET Core)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApiMcp(options =>
{
    options.SpecPath = "petstore.yaml";
    options.TargetApiBaseUrl = "https://api.example.com";
});

builder.Services.AddMcpServer()
    .WithOpenApiTools()
    .WithHttpTransport();

var app = builder.Build();
app.MapMcp();
app.Run();
```

### Single Endpoint with OAuth2

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApiMcp(options =>
{
    options.SpecPath = "https://api.example.com/openapi.json";
    options.TargetApiBaseUrl = "https://api.example.com";
    options.OAuth2Clients["oauth2"] = new OAuth2ClientConfig
    {
        TokenEndpoint = "https://auth.example.com/oauth/token",
        ClientId = "my-client-id",
        ClientSecret = "my-client-secret",
        Scopes = "read write"
    };
});

builder.Services.AddMcpServer()
    .WithOpenApiTools()
    .WithHttpTransport();

var app = builder.Build();
app.MapMcp();
app.Run();
```

### Multi-Endpoint (appsettings.json)

Configure multiple OpenAPI specs, each exposed as an independent MCP service:

**appsettings.json:**
```json
{
  "OpenApiToMcp": {
    "Endpoints": [
      {
        "Name": "customer-mgmt",
        "SpecPath": "https://petstore3.swagger.io/api/v3/openapi.json",
        "TargetApiBaseUrl": "https://petstore3.swagger.io"
      },
      {
        "Name": "order-api",
        "SpecPath": "./specs/order-api.yaml",
        "TargetApiBaseUrl": "http://order-service:8080",
        "IncludePatterns": ["list*", "get*"],
        "ApiKey": "your-api-key"
      },
      {
        "Name": "secure-api",
        "SpecPath": "./specs/secure-api.yaml",
        "TargetApiBaseUrl": "https://secure.example.com",
        "OAuth2Clients": {
          "oauth2_scheme": {
            "TokenEndpoint": "https://auth.example.com/oauth/token",
            "ClientId": "my-client-id",
            "ClientSecret": "my-client-secret",
            "Scopes": "read write"
          }
        }
      }
    ]
  }
}
```

**Program.cs:**
```csharp
var builder = WebApplication.CreateBuilder(args);

// Load endpoints from appsettings.json "OpenApiToMcp" section
builder.Services.AddOpenApiMcp(builder.Configuration);

builder.Services.AddMcpServer()
    .WithMultiEndpointOpenApiTools()
    .WithHttpTransport();

builder.Services.AddCors();

var app = builder.Build();
app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

// Each endpoint is accessible at /mcp/{Name}
app.MapMcp("/mcp/{endpoint}");

app.Run();
```

This exposes:
- `POST /mcp/customer-mgmt` → MCP tools for customer-mgmt API
- `POST /mcp/order-api` → MCP tools for order-api

### CLI

```bash
dotnet tool install -g OpenApiToMcp.Cli

openapi-to-mcp --spec ./petstore.yaml --targetUrl https://api.example.com
openapi-to-mcp --spec https://api.example.com/openapi.json --transport sse --port 8080
```

## Endpoint Configuration

Each endpoint in the `OpenApiToMcp:Endpoints` array supports:

| Field | Type | Description |
|-------|------|-------------|
| `Name` | string | **Required.** Unique name, used as route segment: `/mcp/{Name}` |
| `SpecPath` | string | **Required.** Path or URL to OpenAPI spec (JSON or YAML) |
| `OverlayPaths` | string[] | Paths or URLs to OpenAPI Overlay files |
| `TargetApiBaseUrl` | string | Override base server URL from spec |
| `IncludePatterns` | string[] | Glob patterns to include operations (by operationId or METHOD:/path) |
| `ExcludePatterns` | string[] | Glob patterns to exclude operations (ignored if IncludePatterns set) |
| `ApiKey` | string | Default API key for authentication |
| `SecurityCredentials` | object | Map of security scheme name → credential |
| `OAuth2Clients` | object | Map of security scheme name → OAuth2 client credentials config |
| `CustomHeaders` | object | Custom HTTP headers for outgoing requests |
| `DisableXMcp` | bool | Disable `X-MCP: 1` header |

### OAuth2Clients Configuration

Each entry in `OAuth2Clients` maps a security scheme name to its OAuth2 client credentials:

| Field | Type | Description |
|-------|------|-------------|
| `TokenEndpoint` | string | **Required.** OAuth2 token endpoint URL |
| `ClientId` | string | **Required.** OAuth2 client ID |
| `ClientSecret` | string | **Required.** OAuth2 client secret |
| `Scopes` | string | Optional space-delimited scopes |
| `GrantType` | string | Grant type, default `client_credentials` |

**Credential resolution order** per security scheme:
1. `OAuth2Clients` — dynamic OAuth2 token exchange with auto-refresh (30s pre-expiry buffer)
2. `SecurityCredentials` — static credential from the dictionary
3. `ApiKey` — fallback to the default API key

## Features

- **OpenAPI 3.x** specification support (JSON and YAML)
- **Multi-endpoint**: serve multiple API specs as independent MCP services from one host
- **Overlay Specification v1.0.0** for modifying specs without changing the original
- **Operation filtering** via include/exclude glob patterns
- **x-mcp extensions** for custom tool names and descriptions
- **Security schemes**: API key, HTTP Basic/Bearer, OAuth2 client credentials (with auto-refresh), OpenID Connect
- **Request/Response schema conversion** to JSON Schema 7
- **Transports**: stdio, HTTP/SSE

## License

MIT
