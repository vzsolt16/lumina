using System.Text.Json.Serialization;
using Lumina.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumina.Tests;

public class AiJsonParserTests
{
    private record SimpleDto(string Name, int Value);

    private record NestedDto(
        [property: JsonPropertyName("questions")] List<string> Questions);

    // --- happy path ---

    [Fact]
    public void Parse_ValidJson_ReturnsDeserializedObject()
    {
        var json = """{"name":"hello","value":42}""";

        var result = AiJsonParser.Parse<SimpleDto>(json, NullLogger.Instance);

        Assert.Equal("hello", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Parse_JsonWithSurroundingProse_ExtractsCurlyBraceContent()
    {
        var json = """Here is the answer: {"name":"foo","value":7} Hope that helps!""";

        var result = AiJsonParser.Parse<SimpleDto>(json, NullLogger.Instance);

        Assert.Equal("foo", result.Name);
        Assert.Equal(7, result.Value);
    }

    [Fact]
    public void Parse_JsonWithThinkBlock_StripsThinkBlockFirst()
    {
        var json = "<think>Reasoning step one.\nReasoning step two.</think>{\"name\":\"bar\",\"value\":3}";

        var result = AiJsonParser.Parse<SimpleDto>(json, NullLogger.Instance);

        Assert.Equal("bar", result.Name);
        Assert.Equal(3, result.Value);
    }

    [Fact]
    public void Parse_ThinkBlockAndSurroundingProse_HandlesBoth()
    {
        var json = "<think>think</think>Here you go: {\"name\":\"baz\",\"value\":99}";

        var result = AiJsonParser.Parse<SimpleDto>(json, NullLogger.Instance);

        Assert.Equal("baz", result.Name);
        Assert.Equal(99, result.Value);
    }

    [Fact]
    public void Parse_PropertyNamesAreCaseInsensitive()
    {
        var json = """{"NAME":"case","VALUE":1}""";

        var result = AiJsonParser.Parse<SimpleDto>(json, NullLogger.Instance);

        Assert.Equal("case", result.Name);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void Parse_NestedDto_DeserializesCorrectly()
    {
        var json = """{"questions":["q1","q2"]}""";

        var result = AiJsonParser.Parse<NestedDto>(json, NullLogger.Instance);

        Assert.Equal(["q1", "q2"], result.Questions);
    }

    // --- failure ---

    [Fact]
    public void Parse_CompletelyInvalidJson_ThrowsInvalidOperationException()
    {
        var bad = "not json at all";

        Assert.Throws<InvalidOperationException>(
            () => AiJsonParser.Parse<SimpleDto>(bad, NullLogger.Instance));
    }

    [Fact]
    public void Parse_WrongSchema_ThrowsInvalidOperationException()
    {
        // Valid JSON but wrong shape for SimpleDto (array, not object).
        var json = """[1, 2, 3]""";

        Assert.Throws<InvalidOperationException>(
            () => AiJsonParser.Parse<SimpleDto>(json, NullLogger.Instance));
    }
}
