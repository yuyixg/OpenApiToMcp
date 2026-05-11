using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace OpenApiToMcp.Core.Overlays;

/// <summary>
/// Applies OpenAPI Overlay modifications to an OpenAPI document.
/// </summary>
public interface IOverlayApplier
{
    /// <summary>
    /// Applies overlay actions (update/remove) to the target document.
    /// </summary>
    OpenApiDocument Apply(OpenApiDocument target, JsonNode overlayDoc);
}
