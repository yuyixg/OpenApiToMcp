using OpenApiToMcp.Core.Filtering;
using OpenApiToMcp.Core.Models;

namespace OpenApiToMcp.Tests;

public class OperationFilterTests
{
    [Fact]
    public void ShouldInclude_ReturnsTrue_WhenNoFilters()
    {
        var options = new OpenApiToMcpOptions();
        Assert.True(OperationFilter.ShouldInclude("listPets", "/pets", "get", options));
    }

    [Fact]
    public void IncludePatterns_IncludesMatching_ExcludesOthers()
    {
        var options = new OpenApiToMcpOptions
        {
            IncludePatterns = new List<string> { "list*", "create*" }
        };

        Assert.True(OperationFilter.ShouldInclude("listPets", "/pets", "get", options));
        Assert.True(OperationFilter.ShouldInclude("createPet", "/pets", "post", options));
        Assert.False(OperationFilter.ShouldInclude("getPetById", "/pets/{id}", "get", options));
    }

    [Fact]
    public void ExcludePatterns_ExcludesMatching_IncludesOthers()
    {
        var options = new OpenApiToMcpOptions
        {
            ExcludePatterns = new List<string> { "delete*" }
        };

        Assert.True(OperationFilter.ShouldInclude("listPets", "/pets", "get", options));
        Assert.False(OperationFilter.ShouldInclude("deletePet", "/pets/{id}", "delete", options));
    }

    [Fact]
    public void IncludePatterns_TakesPriorityOverExcludePatterns()
    {
        var options = new OpenApiToMcpOptions
        {
            IncludePatterns = new List<string> { "list*" },
            ExcludePatterns = new List<string> { "listPets" }
        };

        Assert.True(OperationFilter.ShouldInclude("listPets", "/pets", "get", options));
        Assert.False(OperationFilter.ShouldInclude("createPet", "/pets", "post", options));
    }

    [Fact]
    public void FilterByMethodPath_Pattern()
    {
        var options = new OpenApiToMcpOptions
        {
            IncludePatterns = new List<string> { "GET:**" }
        };

        Assert.True(OperationFilter.ShouldInclude("listPets", "/pets", "get", options));
        Assert.False(OperationFilter.ShouldInclude("createPet", "/pets", "post", options));
    }

    [Fact]
    public void ShouldInclude_WithNullOperationId_UsesMethodPath()
    {
        var options = new OpenApiToMcpOptions
        {
            IncludePatterns = new List<string> { "GET:/pets" }
        };

        Assert.True(OperationFilter.ShouldInclude(null, "/pets", "get", options));
    }
}
