using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenApiToMcp.Core.Execution;
using OpenApiToMcp.Core.Mapping;
using OpenApiToMcp.Core.Models;
using OpenApiToMcp.Core.Parsing;

namespace OpenApiToMcp.AspNetCore;

/// <summary>
/// Extension methods for registering OpenAPI-to-MCP tools on an MCP server builder.
/// </summary>
public static class McpServerBuilderExtensions
{
    /// <summary>
    /// Loads the OpenAPI spec, maps operations to MCP tools, and registers them on the builder.
    /// Single-endpoint mode: must be called after AddOpenApiMcp(Action{OpenApiToMcpOptions}).
    /// </summary>
    public static IMcpServerBuilder WithOpenApiTools(this IMcpServerBuilder builder)
    {
        var sp = builder.Services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<OpenApiToMcpOptions>>().Value;
        var parser = sp.GetRequiredService<IOpenApiSpecLoader>();
        var mapper = sp.GetRequiredService<IOperationMapper>();
        var credentialProvider = sp.GetRequiredService<ICredentialProvider>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("OpenApiToMcp");

        var doc = parser.LoadAndProcessAsync(options).GetAwaiter().GetResult();
        var mappedOps = mapper.MapToMcpTools(doc, options);

        if (mappedOps.Count == 0)
        {
            logger.LogWarning("No tools were mapped from the OpenAPI spec.");
            return builder;
        }

        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var tools = new List<McpServerTool>();
        foreach (var op in mappedOps)
        {
            tools.Add(new OpenApiMcpServerTool(op, options, httpClientFactory, credentialProvider));
        }

        logger.LogInformation("Created {Count} MCP tools from OpenAPI spec.", tools.Count);
        builder.WithTools(tools);

        return builder;
    }

    /// <summary>
    /// Registers MCP tools for multiple OpenAPI endpoints.
    /// Each endpoint in the config is loaded and mapped independently.
    /// Use with MapMcp(config.RoutePattern) to expose each endpoint at a named route.
    /// Multi-endpoint mode: must be called after AddOpenApiMcp(IConfiguration) or AddOpenApiMcp(OpenApiToMcpConfig).
    /// </summary>
    public static IMcpServerBuilder WithMultiEndpointOpenApiTools(this IMcpServerBuilder builder)
    {
        var sp = builder.Services.BuildServiceProvider();
        var config = sp.GetRequiredService<OpenApiToMcpConfig>();
        var parser = sp.GetRequiredService<IOpenApiSpecLoader>();
        var mapper = sp.GetRequiredService<IOperationMapper>();
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("OpenApiToMcp");
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

        if (config.Endpoints.Count == 0)
        {
            logger.LogWarning("No endpoints configured in OpenApiToMcp:Endpoints.");
            return builder;
        }

        // Load all specs and build tool collections per endpoint
        var endpointTools = new Dictionary<string, IReadOnlyList<McpServerTool>>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in config.Endpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.Name))
            {
                logger.LogWarning("Skipping endpoint with empty Name.");
                continue;
            }

            logger.LogInformation("Loading OpenAPI spec for endpoint '{Name}' from: {SpecPath}",
                endpoint.Name, endpoint.SpecPath);

            try
            {
                // Convert McpEndpointConfig to OpenApiToMcpOptions for the parser/mapper
                var options = new OpenApiToMcpOptions
                {
                    SpecPath = endpoint.SpecPath,
                    OverlayPaths = endpoint.OverlayPaths,
                    TargetApiBaseUrl = endpoint.TargetApiBaseUrl,
                    IncludePatterns = endpoint.IncludePatterns,
                    ExcludePatterns = endpoint.ExcludePatterns,
                    SecurityCredentials = endpoint.SecurityCredentials,
                    ApiKey = endpoint.ApiKey,
                    OAuth2Clients = endpoint.OAuth2Clients,
                    CustomHeaders = endpoint.CustomHeaders,
                    DisableXMcp = endpoint.DisableXMcp
                };

                var doc = parser.LoadAndProcessAsync(options).GetAwaiter().GetResult();
                var mappedOps = mapper.MapToMcpTools(doc, options);

                if (mappedOps.Count == 0)
                {
                    logger.LogWarning("No tools mapped for endpoint '{Name}'.", endpoint.Name);
                    endpointTools[endpoint.Name] = Array.Empty<McpServerTool>();
                    continue;
                }

                // Each endpoint gets its own credential provider with its own options
                var credProvider = new DefaultCredentialProvider(
                    options,
                    httpClientFactory,
                    loggerFactory.CreateLogger<DefaultCredentialProvider>());

                var tools = new List<McpServerTool>();
                foreach (var op in mappedOps)
                {
                    tools.Add(new OpenApiMcpServerTool(op, options, httpClientFactory, credProvider));
                }

                endpointTools[endpoint.Name] = tools;
                logger.LogInformation("Endpoint '{Name}': created {Count} MCP tools.", endpoint.Name, tools.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load OpenAPI spec for endpoint '{Name}'.", endpoint.Name);
                endpointTools[endpoint.Name] = Array.Empty<McpServerTool>();
            }
        }

        // Register all tools so they're available in DI.
        // The ConfigureSessionOptions callback will select the right subset per request.
        var allTools = endpointTools.Values.SelectMany(t => t).ToList();
        if (allTools.Count > 0)
            builder.WithTools(allTools);

        // Store the endpoint-to-tools mapping in DI for the endpoint resolver
        var resolver = new EndpointToolResolver(endpointTools);
        builder.Services.AddSingleton(resolver);

        // Configure per-session tool selection based on route
        builder.Services.AddOptions<HttpServerTransportOptions>()
            .Configure<EndpointToolResolver>((httpOpts, resolver) =>
            {
                httpOpts.ConfigureSessionOptions = async (httpContext, mcpOptions, ct) =>
                {
                    var endpointName = httpContext.Request.RouteValues["endpoint"]?.ToString();
                    if (string.IsNullOrEmpty(endpointName))
                        return;

                    if (resolver.TryGetTools(endpointName, out var tools))
                    {
                        mcpOptions.ToolCollection = new McpServerPrimitiveCollection<McpServerTool>();
                        foreach (var tool in tools)
                            mcpOptions.ToolCollection.TryAdd(tool);
                    }
                    else
                    {
                        // Return empty tool set for unknown endpoints
                        mcpOptions.ToolCollection = new McpServerPrimitiveCollection<McpServerTool>();
                    }
                };
            });

        logger.LogInformation("Multi-endpoint MCP configured with {Count} endpoints.", endpointTools.Count);
        return builder;
    }
}

/// <summary>
/// Stores the mapping from endpoint name to its MCP tools.
/// Used by ConfigureSessionOptions to select tools per request.
/// </summary>
public class EndpointToolResolver
{
    private readonly Dictionary<string, IReadOnlyList<McpServerTool>> _endpointTools;

    public EndpointToolResolver(Dictionary<string, IReadOnlyList<McpServerTool>> endpointTools)
    {
        _endpointTools = new Dictionary<string, IReadOnlyList<McpServerTool>>(endpointTools, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets all configured endpoint names.
    /// </summary>
    public IEnumerable<string> EndpointNames => _endpointTools.Keys;

    /// <summary>
    /// Tries to get the tools for a given endpoint name.
    /// </summary>
    public bool TryGetTools(string endpointName, out IReadOnlyList<McpServerTool> tools)
    {
        if (_endpointTools.TryGetValue(endpointName, out var t) && t.Count > 0)
        {
            tools = t;
            return true;
        }
        tools = Array.Empty<McpServerTool>();
        return false;
    }
}

/// <summary>
/// Custom McpServerTool that wraps a MappedOperation, providing the protocol tool
/// definition with pre-built JSON Schema and invoking the API client at runtime.
/// </summary>
internal class OpenApiMcpServerTool : McpServerTool
{
    private readonly MappedOperation _operation;
    private readonly OpenApiToMcpOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialProvider _credentialProvider;

    public OpenApiMcpServerTool(
        MappedOperation operation,
        OpenApiToMcpOptions options,
        IHttpClientFactory httpClientFactory,
        ICredentialProvider credentialProvider)
    {
        _operation = operation;
        _options = options;
        _httpClientFactory = httpClientFactory;
        _credentialProvider = credentialProvider;

        var toolDef = operation.ToolInfo;

        // Convert JsonObject schemas to JsonElement for the Tool type
        var inputSchemaElement = JsonDocument.Parse(toolDef.InputSchema.ToJsonString()).RootElement;

        ProtocolTool = new Tool
        {
            Name = toolDef.Name,
            Description = toolDef.Description,
            InputSchema = inputSchemaElement,
        };

        if (toolDef.OutputSchema != null)
        {
            var outputElement = JsonDocument.Parse(toolDef.OutputSchema.ToJsonString()).RootElement;
            // MCP SDK requires OutputSchema to be type "object"
            if (outputElement.ValueKind == JsonValueKind.Object &&
                outputElement.TryGetProperty("type", out var typeProp) &&
                typeProp.GetString() == "object")
            {
                ProtocolTool.OutputSchema = outputElement;
            }
        }

        // Custom annotations go into Meta (JsonObject)
        if (toolDef.Annotations is { Count: > 0 })
        {
            ProtocolTool.Meta = new JsonObject();
            foreach (var (k, v) in toolDef.Annotations)
                ProtocolTool.Meta[k] = v != null ? JsonValue.Create(v.ToString()) : null;
        }
    }

    public override Tool ProtocolTool { get; }

    public override IReadOnlyList<object> Metadata { get; } = Array.Empty<object>();

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        JsonElement? input = null;
        if (request.Params?.Arguments is { Count: > 0 })
        {
            input = JsonSerializer.SerializeToElement(request.Params.Arguments);
        }

        var httpClient = _httpClientFactory.CreateClient(ApiClient.HttpClientName);
        var apiClient = new ApiClient(httpClient, _options, _credentialProvider);
        // Clone input to avoid issues with JsonSerializerOptions pooled buffer padding
        JsonElement? clonedInput = null;
        if (input is { } inp)
            clonedInput = JsonDocument.Parse(inp.GetRawText()).RootElement;
        var result = await apiClient.ExecuteAsync(_operation.CallInfo, clonedInput, cancellationToken);

        if (result.Success)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = result.Data ?? "{}" }]
            };
        }

        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = result.Error ?? "Unknown error" }]
        };
    }
}
