using OpenApiToMcp.Core.Filtering;

namespace OpenApiToMcp.Tests;

public class GlobMatcherTests
{
    [Theory]
    [InlineData("listPets", "*", true)]
    [InlineData("listPets", "list*", true)]
    [InlineData("listPets", "*Pets", true)]
    [InlineData("listPets", "get*", false)]
    [InlineData("listPets", "listP???", true)]
    [InlineData("POST:/pets", "GET:*", false)]
    [InlineData("GET:/pets/123", "GET:/pets/**", true)]
    [InlineData("GET:/pets/123", "GET:/pets/*", true)]
    [InlineData("GET:/pets/123/sub", "GET:/pets/*", false)]
    [InlineData("GET:/pets/123/sub", "GET:/pets/**", true)]
    [InlineData("listPets", "listPets", true)]
    [InlineData("listPets", "listPets2", false)]
    [InlineData("GET:/pets", "GET:/pets", true)]
    public void Matches_ShouldWork(string text, string pattern, bool expected)
    {
        Assert.Equal(expected, GlobMatcher.Matches(text, pattern));
    }
}
