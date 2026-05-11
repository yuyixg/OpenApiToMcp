using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using OpenApiToMcp.Core.Models;

namespace OpenApiToMcp.Core.Parsing;

/// <summary>
/// Loads, validates, and processes OpenAPI specifications.
/// </summary>
public interface IOpenApiSpecLoader
{
    /// <summary>
    /// Loads and processes an OpenAPI spec: load → validate → apply overlays → return.
    /// </summary>
    Task<OpenApiDocument> LoadAndProcessAsync(OpenApiToMcpOptions options);

    /// <summary>
    /// Loads an OpenAPI document from a file path or HTTP URL.
    /// </summary>
    Task<OpenApiDocument> LoadSpecAsync(string pathOrUrl);

    /// <summary>
    /// Loads an overlay document as raw JsonNode (for the OverlayApplier).
    /// </summary>
    Task<JsonNode> LoadOverlayAsync(string pathOrUrl);

    /// <summary>
    /// Validates that the OpenAPI document has the minimum required structure.
    /// </summary>
    void ValidateSpec(OpenApiDocument doc);
}
