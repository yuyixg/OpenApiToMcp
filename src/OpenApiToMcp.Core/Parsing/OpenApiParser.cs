using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using OpenApiToMcp.Core.Execution;
using OpenApiToMcp.Core.Models;
using OpenApiToMcp.Core.Overlays;

namespace OpenApiToMcp.Core.Parsing;

/// <summary>
/// Loads, validates, and processes OpenAPI specifications.
/// </summary>
public class OpenApiParser : IOpenApiSpecLoader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOverlayApplier _overlayApplier;
    private readonly ILogger<OpenApiParser> _logger;

    public OpenApiParser(IHttpClientFactory httpClientFactory, IOverlayApplier overlayApplier, ILogger<OpenApiParser> logger)
    {
        _httpClientFactory = httpClientFactory;
        _overlayApplier = overlayApplier;
        _logger = logger;
    }

    /// <inheritdoc/>
    public virtual async Task<OpenApiDocument> LoadAndProcessAsync(OpenApiToMcpOptions options)
    {
        var doc = await LoadSpecAsync(options.SpecPath);
        ValidateSpec(doc);

        if (options.OverlayPaths is { Count: > 0 })
        {
            foreach (var overlayPath in options.OverlayPaths)
            {
                try
                {
                    var overlayJson = await LoadOverlayAsync(overlayPath);
                    doc = _overlayApplier.Apply(doc, overlayJson);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply overlay '{OverlayPath}'.", overlayPath);
                }
            }
        }

        if (string.IsNullOrEmpty(options.TargetApiBaseUrl) &&
            (doc.Servers == null || doc.Servers.Count == 0))
        {
            throw new InvalidOperationException(
                "Cannot determine target API URL. Either set TargetApiBaseUrl or include servers in the OpenAPI spec.");
        }

        return doc;
    }

    /// <inheritdoc/>
    public virtual async Task<OpenApiDocument> LoadSpecAsync(string pathOrUrl)
    {
        ReadResult result;

        if (IsHttpUrl(pathOrUrl))
        {
            var httpClient = _httpClientFactory.CreateClient(ApiClient.HttpClientName);
            var response = await httpClient.GetAsync(pathOrUrl);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();

            string? mediaType = (pathOrUrl.EndsWith(".yaml") || pathOrUrl.EndsWith(".yml"))
                ? "yaml" : "json";

            result = await OpenApiDocument.LoadAsync(stream, mediaType);
        }
        else
        {
            if (!File.Exists(pathOrUrl))
                throw new FileNotFoundException($"OpenAPI spec file not found: {pathOrUrl}");

            result = await OpenApiDocument.LoadAsync(Path.GetFullPath(pathOrUrl));
        }

        if (result.Document == null)
            throw new InvalidOperationException($"Failed to parse OpenAPI spec from '{pathOrUrl}'.");

        return result.Document;
    }

    /// <inheritdoc/>
    public virtual async Task<JsonNode> LoadOverlayAsync(string pathOrUrl)
    {
        string content;

        if (IsHttpUrl(pathOrUrl))
        {
            var httpClient = _httpClientFactory.CreateClient(ApiClient.HttpClientName);
            content = await httpClient.GetStringAsync(pathOrUrl);
        }
        else
        {
            if (!File.Exists(pathOrUrl))
                throw new FileNotFoundException($"Overlay file not found: {pathOrUrl}");
            content = await File.ReadAllTextAsync(pathOrUrl);
        }

        if (pathOrUrl.EndsWith(".yaml") || pathOrUrl.EndsWith(".yml"))
        {
            // Parse YAML via stream-based reader
            var yamlReader = new Microsoft.OpenApi.YamlReader.OpenApiYamlReader();
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            var readResult = await yamlReader.ReadAsync(stream, null as Uri, null, CancellationToken.None);
            using var ms = new MemoryStream();
            using var sw = new StreamWriter(ms, leaveOpen: true);
            var writer = new OpenApiJsonWriter(sw);
            await readResult.Document!.SerializeAsync(writer, OpenApiSpecVersion.OpenApi3_0);
            await sw.FlushAsync();
            ms.Position = 0;
            return JsonNode.Parse(ms)!;
        }

        return JsonNode.Parse(content)
            ?? throw new InvalidOperationException($"Failed to parse overlay JSON from '{pathOrUrl}'.");
    }

    /// <inheritdoc/>
    public virtual void ValidateSpec(OpenApiDocument doc)
    {
        if (doc.Info == null)
            throw new InvalidOperationException("OpenAPI spec is missing the required 'info' section.");

        if (doc.Paths == null || doc.Paths.Count == 0)
            throw new InvalidOperationException("OpenAPI spec has no paths defined.");

        bool hasOperation = false;
        foreach (var pathItem in doc.Paths.Values)
        {
            if (pathItem.Operations is { Count: > 0 })
            {
                hasOperation = true;
                break;
            }
        }

        if (!hasOperation)
            throw new InvalidOperationException("OpenAPI spec has no operations in any path.");
    }

    private static bool IsHttpUrl(string value)
        => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
           value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
