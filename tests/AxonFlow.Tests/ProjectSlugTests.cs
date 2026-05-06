namespace AxonFlow.Tests;

public class ProjectSlugTests
{
    [Theory]
    [InlineData("MyApp", "myapp")]
    [InlineData("My-App-Cloned", "my-app-cloned")]
    [InlineData("__weird!!", "weird")]
    [InlineData("  x  ", "x")]
    public void Normalize_produces_stable_slug(string input, string expected) =>
        Assert.Equal(expected, ProjectSlug.Normalize(input));

    [Fact]
    public void DisplayNameFromSlug_title_cases_words() =>
        Assert.Equal("Axon Flow", ProjectSlug.DisplayNameFromSlug("axon-flow"));
}
