using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using OpenApiToMcp.Core.Filtering;
using OpenApiToMcp.Core.Mapping;
using OpenApiToMcp.Core.Models;
using OpenApiToMcp.Core.Overlays;
using OpenApiToMcp.Core.Parsing;

namespace OpenApiToMcp.IntegrationTests;

/// <summary>
/// Integration tests against the Petstore OpenAPI 3.0 spec.
/// Verifies the full parse → map → filter pipeline works on a real-world spec.
/// </summary>
public class PetstoreIntegrationTests
{
    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private const string BaseUrl = "https://petstore3.swagger.io/api/v3";

    private static (IOpenApiSpecLoader Parser, IOperationMapper Mapper) CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddHttpClient("OpenApiToMcp");
        services.AddSingleton<IOverlayApplier, OverlayApplier>();
        services.AddSingleton<IOpenApiSpecLoader, OpenApiParser>();
        services.AddSingleton<IOperationMapper, OperationMapper>();
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IOpenApiSpecLoader>(), sp.GetRequiredService<IOperationMapper>());
    }

    private static async Task<OpenApiDocument> LoadPetstoreAsync()
    {
        var (parser, _) = CreateServices();
        var options = new OpenApiToMcpOptions
        {
            SpecPath = FixturePath("petstore-openapi.json")
        };
        return await parser.LoadAndProcessAsync(options);
    }

    // --- Parsing Tests ---

    [Fact]
    public async Task LoadSpecAsync_Petstore_LoadsSuccessfully()
    {
        var doc = await LoadPetstoreAsync();
        Assert.NotNull(doc);
        Assert.Contains("Petstore", doc.Info.Title);
        Assert.Equal("1.0.27", doc.Info.Version);
        Assert.True(doc.Paths.Count > 5, $"Expected multiple paths, got {doc.Paths.Count}");
    }

    [Fact]
    public async Task ValidateSpec_Petstore_Passes()
    {
        var doc = await LoadPetstoreAsync();
        var (parser, _) = CreateServices();
        parser.ValidateSpec(doc); // Should not throw
    }

    [Fact]
    public async Task LoadSpecAsync_Petstore_HasSecuritySchemes()
    {
        var doc = await LoadPetstoreAsync();
        var schemes = doc.Components?.SecuritySchemes;
        Assert.NotNull(schemes);
        Assert.True(schemes!.ContainsKey("petstore_auth"));
        Assert.True(schemes.ContainsKey("api_key"));

        var apiKey = schemes["api_key"];
        Assert.Equal(SecuritySchemeType.ApiKey, apiKey.Type);
    }

    [Fact]
    public async Task LoadSpecAsync_Petstore_HasServers()
    {
        var doc = await LoadPetstoreAsync();
        Assert.NotNull(doc.Servers);
        Assert.True(doc.Servers!.Count > 0);
    }

    // --- Mapping Tests ---

    [Fact]
    public async Task MapToMcpTools_Petstore_MapsAllOperations()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions { TargetApiBaseUrl = BaseUrl };
        var tools = mapper.MapToMcpTools(doc, options);

        // Petstore has 19 operations
        Assert.True(tools.Count >= 15, $"Expected 15+ tools, got {tools.Count}");

        // All should have unique names
        var names = tools.Select(t => t.ToolInfo.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_EachToolHasInputSchema()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions { TargetApiBaseUrl = BaseUrl };
        var tools = mapper.MapToMcpTools(doc, options);

        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrEmpty(tool.ToolInfo.Name), "Tool name must not be empty");
            Assert.False(string.IsNullOrEmpty(tool.ToolInfo.Description), $"Tool {tool.ToolInfo.Name} must have a description");
            Assert.NotNull(tool.ToolInfo.InputSchema);
            Assert.Equal("object", tool.ToolInfo.InputSchema["type"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_EachToolHasCallInfo()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions { TargetApiBaseUrl = BaseUrl };
        var tools = mapper.MapToMcpTools(doc, options);

        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrEmpty(tool.CallInfo.Method), $"Tool {tool.ToolInfo.Name} must have a method");
            Assert.False(string.IsNullOrEmpty(tool.CallInfo.PathTemplate), $"Tool {tool.ToolInfo.Name} must have a path");
            Assert.Equal(BaseUrl, tool.CallInfo.ServerUrl);
        }
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_HandlesPathParameters()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions { TargetApiBaseUrl = BaseUrl };
        var tools = mapper.MapToMcpTools(doc, options);

        // getPetById has path parameter petId
        var getPet = tools.FirstOrDefault(t => t.ToolInfo.Name == "getPetById");
        Assert.NotNull(getPet);
        Assert.Equal("GET", getPet!.CallInfo.Method);
        Assert.Equal("/pet/{petId}", getPet.CallInfo.PathTemplate);

        var props = getPet.ToolInfo.InputSchema["properties"] as JsonObject;
        Assert.NotNull(props);
        Assert.True(props!.ContainsKey("petId"), "getPetById should have 'petId' parameter");

        var required = getPet.ToolInfo.InputSchema["required"] as JsonArray;
        Assert.NotNull(required);
        Assert.Contains(required!, r => r!.GetValue<string>() == "petId");
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_HandlesRequestBody()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions { TargetApiBaseUrl = BaseUrl };
        var tools = mapper.MapToMcpTools(doc, options);

        // addPet has request body
        var addPet = tools.FirstOrDefault(t => t.ToolInfo.Name == "addPet");
        Assert.NotNull(addPet);

        var props = addPet!.ToolInfo.InputSchema["properties"] as JsonObject;
        Assert.NotNull(props);
        Assert.True(props!.ContainsKey("requestBody"), "addPet should have requestBody parameter");
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_EveryToolHasAnnotation()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions { TargetApiBaseUrl = BaseUrl };
        var tools = mapper.MapToMcpTools(doc, options);

        foreach (var tool in tools)
        {
            Assert.NotNull(tool.ToolInfo.Annotations);
            Assert.True(tool.ToolInfo.Annotations!.ContainsKey("x-openapi-path"));
            Assert.True(tool.ToolInfo.Annotations.ContainsKey("x-openapi-method"));
            Assert.False(string.IsNullOrEmpty(tool.ToolInfo.Annotations["x-openapi-path"]?.ToString()));
        }
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_ServerUrlPropagated()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions { TargetApiBaseUrl = BaseUrl };
        var tools = mapper.MapToMcpTools(doc, options);

        foreach (var tool in tools)
        {
            Assert.Equal(BaseUrl, tool.CallInfo.ServerUrl);
        }
    }

    // --- Filtering Tests ---

    [Fact]
    public async Task MapToMcpTools_IncludePatternsPet_FiltersCorrectly()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions
        {
            TargetApiBaseUrl = BaseUrl,
            IncludePatterns = new List<string> { "*Pet*", "*pet*" }
        };
        var tools = mapper.MapToMcpTools(doc, options);

        Assert.All(tools, t => Assert.Contains("et", t.ToolInfo.Name.ToLowerInvariant()));
        Assert.True(tools.Count >= 3, $"Expected multiple pet operations, got {tools.Count}");
    }

    [Fact]
    public async Task MapToMcpTools_IncludePatternsByMethodPath_FiltersCorrectly()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions
        {
            TargetApiBaseUrl = BaseUrl,
            IncludePatterns = new List<string> { "GET:/pet/**" }
        };
        var tools = mapper.MapToMcpTools(doc, options);

        Assert.All(tools, t =>
        {
            Assert.Equal("GET", t.CallInfo.Method);
            Assert.StartsWith("/pet", t.CallInfo.PathTemplate);
        });
        Assert.True(tools.Count >= 3, $"Expected multiple GET /pet operations, got {tools.Count}");
    }

    [Fact]
    public async Task MapToMcpTools_ExcludePatternsByName_ExcludesCorrectly()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var optionsNoFilter = new OpenApiToMcpOptions { TargetApiBaseUrl = BaseUrl };
        var allTools = mapper.MapToMcpTools(doc, optionsNoFilter);

        var options = new OpenApiToMcpOptions
        {
            TargetApiBaseUrl = BaseUrl,
            ExcludePatterns = new List<string> { "delete*" }
        };
        var filteredTools = mapper.MapToMcpTools(doc, options);

        Assert.True(filteredTools.Count < allTools.Count);
        Assert.DoesNotContain(filteredTools, t => t.ToolInfo.Name.StartsWith("delete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MapToMcpTools_IncludePatternsMultiple_CombinesResults()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions
        {
            TargetApiBaseUrl = BaseUrl,
            IncludePatterns = new List<string> { "getInventory", "loginUser" }
        };
        var tools = mapper.MapToMcpTools(doc, options);

        Assert.True(tools.Count >= 2);
        Assert.Contains(tools, t => t.ToolInfo.Name == "getInventory");
        Assert.Contains(tools, t => t.ToolInfo.Name == "loginUser");
    }

    // --- Schema Conversion Tests ---

    [Fact]
    public async Task MapToMcpTools_Petstore_PathParamHasXParameterLocation()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions { TargetApiBaseUrl = BaseUrl };
        var tools = mapper.MapToMcpTools(doc, options);

        var getPet = tools.First(t => t.ToolInfo.Name == "getPetById");
        var props = getPet.ToolInfo.InputSchema["properties"] as JsonObject;
        var petIdProp = props!["petId"] as JsonObject;

        Assert.NotNull(petIdProp);
        Assert.Equal("path", petIdProp!["x-parameter-location"]!.GetValue<string>());
    }

    [Fact]
    public async Task MapToMcpTools_Petstore_QueryParamHasXParameterLocation()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions { TargetApiBaseUrl = BaseUrl };
        var tools = mapper.MapToMcpTools(doc, options);

        var findPets = tools.FirstOrDefault(t => t.ToolInfo.Name == "findPetsByStatus");
        Assert.NotNull(findPets);

        var props = findPets!.ToolInfo.InputSchema["properties"] as JsonObject;
        Assert.NotNull(props);

        var statusProp = props!["status"] as JsonObject;
        Assert.NotNull(statusProp);
        Assert.Equal("query", statusProp!["x-parameter-location"]!.GetValue<string>());
    }

    // --- HttpClient Security Tests ---

    [Fact]
    public async Task MapToMcpTools_Petstore_SecuritySchemePropagated()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions
        {
            TargetApiBaseUrl = BaseUrl,
            ApiKey = "test-api-key"
        };
        var tools = mapper.MapToMcpTools(doc, options);

        // Some operations have petstore_auth or api_key security requirements
        var opsWithSecurity = tools.Where(t =>
            t.CallInfo.SecurityRequirements != null && t.CallInfo.SecurityRequirements.Count > 0).ToList();

        Assert.True(opsWithSecurity.Count > 0, "Some operations should have security requirements");
    }

    // --- Output Schema Tests ---

    [Fact]
    public async Task MapToMcpTools_Petstore_OutputSchemaStructure()
    {
        var doc = await LoadPetstoreAsync();
        var (_, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions { TargetApiBaseUrl = BaseUrl };
        var tools = mapper.MapToMcpTools(doc, options);

        var withOutput = tools.Where(t => t.ToolInfo.OutputSchema != null).ToList();
        foreach (var tool in withOutput)
        {
            Assert.NotNull(tool.ToolInfo.OutputSchema);
            Assert.True(tool.ToolInfo.OutputSchema!.ContainsKey("type"),
                $"Output schema of {tool.ToolInfo.Name} should have a type field");
        }
    }

    // --- Complete Pipeline Test ---

    [Fact]
    public async Task FullPipeline_LoadMapFilter_Petstore()
    {
        var (parser, mapper) = CreateServices();
        var options = new OpenApiToMcpOptions
        {
            SpecPath = FixturePath("petstore-openapi.json"),
            TargetApiBaseUrl = BaseUrl,
            IncludePatterns = new List<string> { "getPetById", "addPet", "getInventory" },
            ApiKey = "test-api-key"
        };

        // Step 1: Load and process
        var doc = await parser.LoadAndProcessAsync(options);
        Assert.NotNull(doc);

        // Step 2: Map
        var tools = mapper.MapToMcpTools(doc, options);
        Assert.Equal(3, tools.Count);

        // Step 3: Verify all tools are well-formed
        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrEmpty(tool.ToolInfo.Name));
            Assert.False(string.IsNullOrEmpty(tool.ToolInfo.Description));
            Assert.NotNull(tool.ToolInfo.InputSchema);
            Assert.False(string.IsNullOrEmpty(tool.CallInfo.Method));
            Assert.False(string.IsNullOrEmpty(tool.CallInfo.PathTemplate));
            Assert.False(string.IsNullOrEmpty(tool.CallInfo.ServerUrl));
        }

        // Step 4: Verify tool names match include patterns
        var names = tools.Select(t => t.ToolInfo.Name).ToHashSet();
        Assert.Contains("getPetById", names);
        Assert.Contains("addPet", names);
        Assert.Contains("getInventory", names);
    }
}
