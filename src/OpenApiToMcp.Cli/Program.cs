using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using OpenApiToMcp.AspNetCore;
using OpenApiToMcp.Core.Models;

var builder = WebApplication.CreateSlimBuilder(args);

// Parse command line arguments
string? specPath = null;
string? targetUrl = null;
string? whitelist = null;
string? blacklist = null;
string? apiKey = null;
string? securitySchemeName = null;
string? securityCredentialsJson = null;
string? headersJson = null;
bool disableXMcp = false;
string? overlayPaths = null;
int port = 8080;
string transport = "stdio";

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--spec" or "-s":
            specPath = NextArg(args, ref i, "--spec");
            break;
        case "--overlays" or "-o":
            overlayPaths = NextArg(args, ref i, "--overlays");
            break;
        case "--targetUrl" or "-u":
            targetUrl = NextArg(args, ref i, "--targetUrl");
            break;
        case "--whitelist" or "-w":
            whitelist = NextArg(args, ref i, "--whitelist");
            break;
        case "--blacklist" or "-b":
            blacklist = NextArg(args, ref i, "--blacklist");
            break;
        case "--apiKey":
            apiKey = NextArg(args, ref i, "--apiKey");
            break;
        case "--securitySchemeName":
            securitySchemeName = NextArg(args, ref i, "--securitySchemeName");
            break;
        case "--securityCredentials":
            securityCredentialsJson = NextArg(args, ref i, "--securityCredentials");
            break;
        case "--headers":
            headersJson = NextArg(args, ref i, "--headers");
            break;
        case "--disableXMcp":
            disableXMcp = true;
            break;
        case "--port" or "-p":
            var portStr = NextArg(args, ref i, "--port");
            if (int.TryParse(portStr, out var p)) port = p;
            break;
        case "--transport" or "-t":
            transport = NextArg(args, ref i, "--transport") ?? "stdio";
            break;
    }
}

// Also check environment variables
specPath ??= Environment.GetEnvironmentVariable("OPENAPI_SPEC_PATH");
targetUrl ??= Environment.GetEnvironmentVariable("TARGET_API_BASE_URL");
overlayPaths ??= Environment.GetEnvironmentVariable("OPENAPI_OVERLAY_PATHS");
whitelist ??= Environment.GetEnvironmentVariable("MCP_WHITELIST_OPERATIONS");
blacklist ??= Environment.GetEnvironmentVariable("MCP_BLACKLIST_OPERATIONS");
apiKey ??= Environment.GetEnvironmentVariable("API_KEY");
securitySchemeName ??= Environment.GetEnvironmentVariable("SECURITY_SCHEME_NAME");
securityCredentialsJson ??= Environment.GetEnvironmentVariable("SECURITY_CREDENTIALS");
headersJson ??= Environment.GetEnvironmentVariable("CUSTOM_HEADERS");

if (Environment.GetEnvironmentVariable("DISABLE_X_MCP") == "true")
    disableXMcp = true;

if (string.IsNullOrEmpty(specPath))
{
    // Use stderr directly for usage text before DI is available
    Console.Error.WriteLine("Error: OpenAPI specification path is required.");
    Console.Error.WriteLine("Usage: openapi-to-mcp --spec <path-or-url> [options]");
    Console.Error.WriteLine("  --spec, -s        Path or URL to OpenAPI spec (required)");
    Console.Error.WriteLine("  --overlays, -o    Comma-separated overlay paths or URLs");
    Console.Error.WriteLine("  --targetUrl, -u   Override API base URL");
    Console.Error.WriteLine("  --whitelist, -w   Comma-separated glob patterns to include");
    Console.Error.WriteLine("  --blacklist, -b   Comma-separated glob patterns to exclude");
    Console.Error.WriteLine("  --apiKey          API key for target API");
    Console.Error.WriteLine("  --headers         JSON string of custom headers");
    Console.Error.WriteLine("  --disableXMcp     Disable X-MCP: 1 header");
    Console.Error.WriteLine("  --transport, -t   Transport: stdio (default) or sse");
    Console.Error.WriteLine("  --port, -p        Port for SSE transport (default: 8080)");
    return 1;
}

// Parse security credentials
var securityCredentials = new Dictionary<string, string>();
if (!string.IsNullOrEmpty(securityCredentialsJson))
{
    try { securityCredentials = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(securityCredentialsJson)!; }
    catch { Console.Error.WriteLine("Warning: Failed to parse --securityCredentials JSON"); }
}

// Parse custom headers
var customHeaders = new Dictionary<string, string>();
if (!string.IsNullOrEmpty(headersJson))
{
    try { customHeaders = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson)!; }
    catch { Console.Error.WriteLine("Warning: Failed to parse --headers JSON"); }
}

// Parse overlay paths
var overlayList = string.IsNullOrEmpty(overlayPaths)
    ? new List<string>()
    : overlayPaths.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

// Parse whitelist/blacklist
List<string>? whitelistPatterns = string.IsNullOrEmpty(whitelist)
    ? null
    : whitelist.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

List<string>? blacklistPatterns = string.IsNullOrEmpty(blacklist)
    ? null
    : blacklist.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

// Configure services
builder.Services.AddOpenApiMcp(options =>
{
    options.SpecPath = specPath;
    options.OverlayPaths = overlayList;
    options.TargetApiBaseUrl = targetUrl;
    options.Whitelist = whitelistPatterns;
    options.Blacklist = blacklistPatterns;
    options.ApiKey = apiKey;
    options.SecuritySchemeName = securitySchemeName;
    options.SecurityCredentials = securityCredentials;
    options.CustomHeaders = customHeaders;
    options.DisableXMcp = disableXMcp;
});

var mcpBuilder = builder.Services
    .AddMcpServer()
    .WithOpenApiTools();

if (transport == "sse")
{
    mcpBuilder.WithHttpTransport();
    builder.Services.AddCors();
}
else
{
    mcpBuilder.WithStdioServerTransport();
}

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OpenApiToMcp.Cli");

if (transport == "sse")
{
    app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
    app.MapMcp();
    logger.LogInformation("MCP server starting on port {Port}...", port);
    app.Run($"http://0.0.0.0:{port}");
}
else
{
    app.Run();
}

return 0;

static string? NextArg(string[] args, ref int i, string flag)
{
    if (i + 1 >= args.Length)
    {
        Console.Error.WriteLine($"Error: {flag} requires a value.");
        return null;
    }
    return args[++i];
}
