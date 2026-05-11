using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using OpenApiToMcp.Core.Overlays;

namespace OpenApiToMcp.Tests;

public class OverlayApplierTests
{
    private static async Task<OpenApiDocument> LoadFixtureAsync(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        var result = await OpenApiDocument.LoadAsync(path);
        Assert.NotNull(result.Document);
        return result.Document!;
    }

    private static JsonNode LoadOverlayFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        var content = File.ReadAllText(path);
        return JsonNode.Parse(content)!;
    }

    [Fact]
    public void Apply_ThrowsOnMissingOverlayVersion()
    {
        var applier = new OverlayApplier();
        var overlay = JsonNode.Parse("""{"actions": []}""")!;
        var doc = new OpenApiDocument();

        Assert.Throws<ArgumentException>(() => applier.Apply(doc, overlay));
    }

    [Fact]
    public void Apply_ReturnsTargetUnchanged_WhenNoActions()
    {
        var applier = new OverlayApplier();
        var overlay = JsonNode.Parse("""{"overlay": "1.0.0", "actions": []}""")!;
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "Test", Version = "1.0.0" },
            Paths = new OpenApiPaths()
        };

        var result = applier.Apply(doc, overlay);
        Assert.NotNull(result);
        Assert.Equal("Test", result.Info.Title);
    }

    [Fact]
    public async Task Apply_PetstoreOverlay_UpdatesInfo()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var overlay = LoadOverlayFixture("petstore-overlay.json");

        var applier = new OverlayApplier();
        var result = applier.Apply(doc, overlay);

        Assert.Equal("Modified Petstore API", result.Info.Title);
        Assert.Equal("1.1.0", result.Info.Version);
    }

    [Fact]
    public async Task Apply_PetstoreOverlay_UpdatesOperationSummary()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var overlay = LoadOverlayFixture("petstore-overlay.json");

        var applier = new OverlayApplier();
        var result = applier.Apply(doc, overlay);

        var listPets = result.Paths["/pets"]!.Operations[HttpMethod.Get];
        Assert.Equal("List all pets with overlay", listPets.Summary);
    }

    [Fact]
    public async Task Apply_PetstoreOverlay_UpdatesParameterDescription()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var overlay = LoadOverlayFixture("petstore-overlay.json");

        var applier = new OverlayApplier();
        var result = applier.Apply(doc, overlay);

        var getPet = result.Paths["/pets/{petId}"]!.Operations[HttpMethod.Get];
        var petIdParam = getPet.Parameters.First(p => p.Name == "petId");
        Assert.Equal("Enhanced pet ID description from overlay", petIdParam.Description);
    }

    [Fact]
    public void Apply_WithRemoveAction_RemovesNode()
    {
        var applier = new OverlayApplier();

        // Create a minimal valid OpenAPI doc
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "Test", Version = "1.0.0" },
            Paths = new OpenApiPaths
            {
                ["/items"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = "listItems",
                            Summary = "List items",
                            Responses = new OpenApiResponses()
                        },
                        [HttpMethod.Post] = new OpenApiOperation
                        {
                            OperationId = "createItem",
                            Summary = "Create item",
                            Responses = new OpenApiResponses()
                        }
                    }
                }
            }
        };

        var overlay = JsonNode.Parse("""
        {
            "overlay": "1.0.0",
            "actions": [
                {
                    "target": "$.paths['/items'].post",
                    "remove": true
                }
            ]
        }
        """)!;

        var result = applier.Apply(doc, overlay);
        var pathItem = result.Paths["/items"]!;
        Assert.Single(pathItem.Operations);
        Assert.True(pathItem.Operations.ContainsKey(HttpMethod.Get));
        Assert.False(pathItem.Operations.ContainsKey(HttpMethod.Post));
    }
}
