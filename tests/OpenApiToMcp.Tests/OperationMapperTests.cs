using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using OpenApiToMcp.Core.Mapping;
using OpenApiToMcp.Core.Models;

namespace OpenApiToMcp.Tests;

public class OperationMapperTests
{
    private readonly IOperationMapper _mapper = TestHelpers.CreateMapper();

    private static async Task<OpenApiDocument> LoadFixtureAsync(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        var result = await OpenApiDocument.LoadAsync(path);
        Assert.NotNull(result.Document);
        return result.Document!;
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_MapsAllOperations()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions();

        var tools = _mapper.MapToMcpTools(doc, options);

        Assert.Equal(3, tools.Count);

        var names = tools.Select(t => t.ToolInfo.Name).OrderBy(n => n).ToList();
        Assert.Contains("createPet", names);
        Assert.Contains("getPetById", names);
        Assert.Contains("listPets", names);
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_ListPetsHasCorrectSchema()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions();

        var tools = _mapper.MapToMcpTools(doc, options);
        var listPets = tools.First(t => t.ToolInfo.Name == "listPets");

        Assert.Equal("List all pets", listPets.ToolInfo.Description);

        var inputSchema = listPets.ToolInfo.InputSchema;
        Assert.Equal("object", inputSchema["type"]!.GetValue<string>());

        var props = inputSchema["properties"] as JsonObject;
        Assert.NotNull(props);
        Assert.True(props!.ContainsKey("limit"));
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_CreatePetHasRequestBody()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions();

        var tools = _mapper.MapToMcpTools(doc, options);
        var createPet = tools.First(t => t.ToolInfo.Name == "createPet");

        var props = createPet.ToolInfo.InputSchema["properties"] as JsonObject;
        Assert.NotNull(props);
        Assert.True(props!.ContainsKey("requestBody"));

        var required = createPet.ToolInfo.InputSchema["required"] as JsonArray;
        Assert.NotNull(required);
        Assert.Contains(required!, r => r!.GetValue<string>() == "requestBody");
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_GetPetById_HasPathParameter()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions();

        var tools = _mapper.MapToMcpTools(doc, options);
        var getPet = tools.First(t => t.ToolInfo.Name == "getPetById");

        var props = getPet.ToolInfo.InputSchema["properties"] as JsonObject;
        Assert.NotNull(props);
        Assert.True(props!.ContainsKey("petId"));

        var required = getPet.ToolInfo.InputSchema["required"] as JsonArray;
        Assert.NotNull(required);
        Assert.Contains(required!, r => r!.GetValue<string>() == "petId");
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_HasOutputSchema()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions();

        var tools = _mapper.MapToMcpTools(doc, options);
        var listPets = tools.First(t => t.ToolInfo.Name == "listPets");

        Assert.NotNull(listPets.ToolInfo.OutputSchema);
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_HasAnnotations()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions();

        var tools = _mapper.MapToMcpTools(doc, options);
        var listPets = tools.First(t => t.ToolInfo.Name == "listPets");

        Assert.NotNull(listPets.ToolInfo.Annotations);
        Assert.Equal("/pets", listPets.ToolInfo.Annotations!["x-openapi-path"]);
        Assert.Equal("GET", listPets.ToolInfo.Annotations["x-openapi-method"]);
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_CallInfoHasCorrectDetails()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions();

        var tools = _mapper.MapToMcpTools(doc, options);
        var getPet = tools.First(t => t.ToolInfo.Name == "getPetById");

        Assert.Equal("GET", getPet.CallInfo.Method);
        Assert.Equal("/pets/{petId}", getPet.CallInfo.PathTemplate);
        Assert.Equal("http://localhost:3000/api", getPet.CallInfo.ServerUrl);
    }

    [Fact]
    public async Task MapToMcpTools_Whitelist_FiltersOperations()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions
        {
            Whitelist = new List<string> { "listPets" }
        };

        var tools = _mapper.MapToMcpTools(doc, options);

        Assert.Single(tools);
        Assert.Equal("listPets", tools[0].ToolInfo.Name);
    }

    [Fact]
    public async Task MapToMcpTools_Blacklist_FiltersOperations()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions
        {
            Blacklist = new List<string> { "createPet" }
        };

        var tools = _mapper.MapToMcpTools(doc, options);

        Assert.Equal(2, tools.Count);
        Assert.DoesNotContain(tools, t => t.ToolInfo.Name == "createPet");
    }

    [Fact]
    public async Task MapToMcpTools_CustomBaseUrl_OverridesServerUrl()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions
        {
            TargetApiBaseUrl = "https://custom.api.com/v2"
        };

        var tools = _mapper.MapToMcpTools(doc, options);

        Assert.All(tools, t => Assert.Equal("https://custom.api.com/v2", t.CallInfo.ServerUrl));
    }

    [Fact]
    public async Task MapToMcpTools_XMcp_OverridesNameAndDescription()
    {
        var doc = await LoadFixtureAsync("petstore-x-mcp.json");
        var options = new OpenApiToMcpOptions();

        var tools = _mapper.MapToMcpTools(doc, options);

        var customListPets = tools.FirstOrDefault(t => t.ToolInfo.Name == "CustomListPets");
        Assert.NotNull(customListPets);
        Assert.Equal("Enhanced listing of all available pets", customListPets!.ToolInfo.Description);
    }

    [Fact]
    public async Task MapToMcpTools_XMcp_PathLevelFallback()
    {
        var doc = await LoadFixtureAsync("petstore-x-mcp.json");
        var options = new OpenApiToMcpOptions();

        var tools = _mapper.MapToMcpTools(doc, options);

        var customPetOps = tools.FirstOrDefault(t => t.ToolInfo.Name == "CustomPetOperations");
        Assert.NotNull(customPetOps);
        Assert.Equal("Path-level description for all operations on a specific pet", customPetOps!.ToolInfo.Description);
    }
}
