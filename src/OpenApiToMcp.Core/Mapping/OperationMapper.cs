using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using OpenApiToMcp.Core.Filtering;
using OpenApiToMcp.Core.Models;

namespace OpenApiToMcp.Core.Mapping;

/// <summary>
/// Maps OpenAPI operations to MCP tool definitions.
/// </summary>
public class OperationMapper
{
    private readonly ILogger<OperationMapper> _logger;

    public OperationMapper(ILogger<OperationMapper> logger)
    {
        _logger = logger;
    }

    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "put", "post", "delete", "options", "head", "patch", "trace"
    };

    /// <summary>
    /// Maps all matching OpenAPI operations to MCP tool definitions.
    /// </summary>
    public List<MappedOperation> MapToMcpTools(OpenApiDocument doc, OpenApiToMcpOptions options)
    {
        var results = new List<MappedOperation>();
        var globalSecurity = doc.Security;
        var securitySchemes = doc.Components?.SecuritySchemes;

        string baseServerUrl = options.TargetApiBaseUrl
            ?? doc.Servers?.FirstOrDefault()?.Url
            ?? "/";
        baseServerUrl = baseServerUrl.TrimEnd('/');

        foreach (var (path, pathItem) in doc.Paths)
        {
            foreach (var (methodKey, operation) in pathItem.Operations)
            {
                var method = methodKey.ToString().ToUpperInvariant();
                var methodLower = methodKey.ToString().ToLowerInvariant();

                if (!HttpMethods.Contains(methodLower))
                    continue;

                if (!OperationFilter.ShouldInclude(operation.OperationId, path, methodLower, options))
                {
                    _logger.LogDebug("Skipping operation {OperationId} due to filter rules.", operation.OperationId ?? $"{method} {path}");
                    continue;
                }

                if (string.IsNullOrEmpty(operation.OperationId))
                {
                    _logger.LogWarning("Skipping operation {Method} {Path} due to missing operationId.", method, path);
                    continue;
                }

                var tool = MapSingleOperation(operation, pathItem, path, method, baseServerUrl,
                    globalSecurity, securitySchemes);

                results.Add(tool);
                _logger.LogDebug("Mapped tool: {ToolName} ({Method} {Path})", tool.ToolInfo.Name, method, path);
            }
        }

        _logger.LogInformation("Total tools mapped: {Count}", results.Count);
        return results;
    }

    private static MappedOperation MapSingleOperation(
        OpenApiOperation operation,
        IOpenApiPathItem pathItem,
        string path,
        string method,
        string baseServerUrl,
        IList<OpenApiSecurityRequirement>? globalSecurity,
        IDictionary<string, IOpenApiSecurityScheme>? securitySchemes)
    {
        // --- Tool Name ---
        string toolName = operation.OperationId!;

        var opMcpExt = GetMcpExtension(operation.Extensions);
        var pathMcpExt = GetMcpExtension(pathItem.Extensions);

        if (opMcpExt != null && opMcpExt.ContainsKey("name"))
            toolName = opMcpExt["name"]!.GetValue<string>();
        else if (pathMcpExt != null && pathMcpExt.ContainsKey("name"))
            toolName = pathMcpExt["name"]!.GetValue<string>();

        // --- Description ---
        string toolDescription =
            operation.Description ??
            operation.Summary ??
            pathItem.Summary ??
            "No description available.";

        if (opMcpExt != null && opMcpExt.ContainsKey("description"))
            toolDescription = opMcpExt["description"]!.GetValue<string>();
        else if (pathMcpExt != null && pathMcpExt.ContainsKey("description"))
            toolDescription = pathMcpExt["description"]!.GetValue<string>();

        // --- Input Schema ---
        var inputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject()
        };

        var allParameters = new List<OpenApiParameter>();
        if (pathItem.Parameters != null)
        {
            foreach (var p in pathItem.Parameters)
            {
                if (p is OpenApiParameter concrete)
                    allParameters.Add(concrete);
            }
        }
        if (operation.Parameters != null)
        {
            foreach (var p in operation.Parameters)
            {
                if (p is OpenApiParameter concrete)
                    allParameters.Add(concrete);
            }
        }

        var dedupedParams = allParameters
            .GroupBy(p => (p.Name, p.In?.ToString()?.ToLowerInvariant()))
            .Select(g => g.Last())
            .ToList();

        var properties = (JsonObject)inputSchema["properties"]!;
        var requiredList = new JsonArray();

        foreach (var param in dedupedParams)
        {
            var (name, schema) = SchemaConverter.ParameterToJsonSchema(param);
            properties[name] = JsonNode.Parse(schema.ToJsonString());

            if (param.Required)
                requiredList.Add(name);
        }

        // --- Request Body ---
        OpenApiRequestBody? concreteRequestBody = null;
        if (operation.RequestBody != null)
        {
            concreteRequestBody = operation.RequestBody as OpenApiRequestBody;
            if (concreteRequestBody != null)
            {
                var jsonContent = concreteRequestBody.Content
                    .FirstOrDefault(c => c.Key == "application/json").Value
                    ?? concreteRequestBody.Content.FirstOrDefault().Value;

                if (jsonContent?.Schema != null)
                {
                    var bodySchema = SchemaConverter.ToJsonSchema(jsonContent.Schema);
                    if (bodySchema != null)
                    {
                        var bodyObj = JsonNode.Parse(bodySchema.ToJsonString()) as JsonObject ?? new JsonObject();

                        if (!string.IsNullOrEmpty(concreteRequestBody.Description))
                            bodyObj["description"] = concreteRequestBody.Description;

                        var contentTypes = new JsonArray();
                        foreach (var ct in concreteRequestBody.Content.Keys)
                            contentTypes.Add(ct);
                        bodyObj["x-content-types"] = contentTypes;

                        properties["requestBody"] = bodyObj;

                        if (concreteRequestBody.Required)
                            requiredList.Add("requestBody");
                    }
                }
            }
        }

        if (requiredList.Count > 0)
            inputSchema["required"] = requiredList;

        // --- Output Schema ---
        JsonObject? outputSchema = null;
        var successResponse = operation.Responses
            .FirstOrDefault(r => r.Key.StartsWith("2"));

        if (successResponse.Value?.Content != null)
        {
            var jsonResp = successResponse.Value.Content
                .FirstOrDefault(c => c.Key == "application/json").Value;

            if (jsonResp?.Schema != null)
            {
                outputSchema = SchemaConverter.ToJsonSchema(jsonResp.Schema) as JsonObject;
                if (outputSchema != null && !string.IsNullOrEmpty(successResponse.Value.Description))
                    outputSchema["description"] = successResponse.Value.Description;
            }
        }

        // --- Assemble ---
        var toolInfo = new McpToolInfo
        {
            Name = toolName,
            Description = toolDescription,
            InputSchema = inputSchema,
            OutputSchema = outputSchema,
            Annotations = new Dictionary<string, object?>
            {
                ["x-openapi-path"] = path,
                ["x-openapi-method"] = method
            }
        };

        IList<OpenApiSecurityRequirement>? secReqs = null;
        if (operation.Security != null && operation.Security.Count > 0)
            secReqs = operation.Security;
        else if (globalSecurity != null && globalSecurity.Count > 0)
            secReqs = globalSecurity;

        // Convert IOpenApiSecurityScheme dict to concrete for ApiCallInfo
        IDictionary<string, OpenApiSecurityScheme>? concreteSchemes = null;
        if (securitySchemes != null)
        {
            concreteSchemes = new Dictionary<string, OpenApiSecurityScheme>();
            foreach (var (k, v) in securitySchemes)
            {
                if (v is OpenApiSecurityScheme concrete)
                    concreteSchemes[k] = concrete;
            }
        }

        var callInfo = new ApiCallInfo
        {
            Method = method,
            PathTemplate = path,
            ServerUrl = baseServerUrl,
            Parameters = dedupedParams,
            RequestBody = concreteRequestBody,
            SecurityRequirements = secReqs,
            SecuritySchemes = concreteSchemes
        };

        return new MappedOperation { ToolInfo = toolInfo, CallInfo = callInfo };
    }

    private static JsonObject? GetMcpExtension(IDictionary<string, IOpenApiExtension>? extensions)
    {
        if (extensions == null || !extensions.TryGetValue("x-mcp", out var ext) || ext == null)
            return null;

        try
        {
            using var ms = new MemoryStream();
            using var sw = new StreamWriter(ms, leaveOpen: true);
            var writer = new OpenApiJsonWriter(sw);
            ext.Write(writer, OpenApiSpecVersion.OpenApi3_0);
            sw.Flush();
            ms.Position = 0;
            var node = JsonNode.Parse(ms);
            if (node is JsonObject jo)
                return jo;
        }
        catch { }

        return null;
    }
}
