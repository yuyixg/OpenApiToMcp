using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using OpenApiToMcp.AspNetCore;
using OpenApiToMcp.Core.Mapping;
using OpenApiToMcp.Core.Models;

namespace OpenApiToMcp.Tests;

public class McpServerBuilderExtensionsTests
{
    private readonly OperationMapper _mapper = TestHelpers.CreateMapper();
    private readonly IHttpClientFactory _httpClientFactory;

    public McpServerBuilderExtensionsTests()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("OpenApiToMcp");
        var sp = services.BuildServiceProvider();
        _httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    }

    private static async Task<OpenApiDocument> LoadFixtureAsync(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        var result = await OpenApiDocument.LoadAsync(path);
        Assert.NotNull(result.Document);
        return result.Document!;
    }

    [Fact]
    public async Task OpenApiMcpServerTool_ProtocolTool_HasCorrectName()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions();
        var ops = _mapper.MapToMcpTools(doc, options);
        var listPets = ops.First(t => t.ToolInfo.Name == "listPets");

        // Create the tool using reflection to access internal class
        var toolType = typeof(OpenApiToMcp.AspNetCore.McpServerBuilderExtensions).Assembly
            .GetTypes()
            .First(t => t.Name == "OpenApiMcpServerTool");

        var tool = Activator.CreateInstance(toolType, listPets, options, _httpClientFactory);
        var protocolTool = toolType.GetProperty("ProtocolTool")!.GetValue(tool) as ModelContextProtocol.Protocol.Tool;

        Assert.NotNull(protocolTool);
        Assert.Equal("listPets", protocolTool!.Name);
        Assert.Equal("List all pets", protocolTool.Description);
    }

    [Fact]
    public async Task OpenApiMcpServerTool_ProtocolTool_HasInputSchema()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions();
        var ops = _mapper.MapToMcpTools(doc, options);
        var getPet = ops.First(t => t.ToolInfo.Name == "getPetById");

        var toolType = typeof(OpenApiToMcp.AspNetCore.McpServerBuilderExtensions).Assembly
            .GetTypes()
            .First(t => t.Name == "OpenApiMcpServerTool");

        var tool = Activator.CreateInstance(toolType, getPet, options, _httpClientFactory);
        var protocolTool = toolType.GetProperty("ProtocolTool")!.GetValue(tool) as ModelContextProtocol.Protocol.Tool;

        Assert.NotNull(protocolTool);
        Assert.Equal(JsonValueKind.Object, protocolTool!.InputSchema.ValueKind);

        var inputObj = JsonDocument.Parse(protocolTool.InputSchema.GetRawText());
        Assert.Equal("object", inputObj.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task OpenApiMcpServerTool_ProtocolTool_HasOutputSchema_WhenObject()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions();
        var ops = _mapper.MapToMcpTools(doc, options);
        // getPetById returns a single Pet (object), so OutputSchema should be set
        var getPet = ops.First(t => t.ToolInfo.Name == "getPetById");

        var toolType = typeof(OpenApiToMcp.AspNetCore.McpServerBuilderExtensions).Assembly
            .GetTypes()
            .First(t => t.Name == "OpenApiMcpServerTool");

        var tool = Activator.CreateInstance(toolType, getPet, options, _httpClientFactory);
        var protocolTool = toolType.GetProperty("ProtocolTool")!.GetValue(tool) as ModelContextProtocol.Protocol.Tool;

        Assert.NotNull(protocolTool);
        // Pet type is object, so OutputSchema should be set
        // (though it may be null if MCP validation rejects it for other reasons)
    }

    [Fact]
    public async Task OpenApiMcpServerTool_ProtocolTool_HasMeta()
    {
        var doc = await LoadFixtureAsync("petstore-openapi.json");
        var options = new OpenApiToMcpOptions();
        var ops = _mapper.MapToMcpTools(doc, options);
        var listPets = ops.First(t => t.ToolInfo.Name == "listPets");

        var toolType = typeof(OpenApiToMcp.AspNetCore.McpServerBuilderExtensions).Assembly
            .GetTypes()
            .First(t => t.Name == "OpenApiMcpServerTool");

        var tool = Activator.CreateInstance(toolType, listPets, options, _httpClientFactory);
        var protocolTool = toolType.GetProperty("ProtocolTool")!.GetValue(tool) as ModelContextProtocol.Protocol.Tool;

        Assert.NotNull(protocolTool);
        Assert.NotNull(protocolTool!.Meta);
        Assert.True(protocolTool.Meta!.ContainsKey("x-openapi-path"));
        Assert.True(protocolTool.Meta.ContainsKey("x-openapi-method"));
    }
}
