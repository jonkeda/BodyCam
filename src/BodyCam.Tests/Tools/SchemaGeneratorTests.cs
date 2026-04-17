using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using BodyCam.Tools;
using FluentAssertions;

namespace BodyCam.Tests.Tools;

public class SchemaGeneratorTests
{
    [Fact]
    public void Generate_EmptyClass_ReturnsObjectWithNoProperties()
    {
        var schema = SchemaGenerator.Generate<EmptyClass>();

        schema.Should().Be("{\"type\":\"object\",\"properties\":{}}");
    }

    [Fact]
    public void Generate_SingleRequiredString_IncludesRequired()
    {
        var schema = SchemaGenerator.Generate<RequiredStringClass>();
        using var doc = JsonDocument.Parse(schema);
        var root = doc.RootElement;

        root.GetProperty("required").EnumerateArray()
            .Should().ContainSingle(e => e.GetString() == "name");
    }

    [Fact]
    public void Generate_NullableProperty_NotRequired()
    {
        var schema = SchemaGenerator.Generate<NullableStringClass>();
        using var doc = JsonDocument.Parse(schema);
        var root = doc.RootElement;

        root.TryGetProperty("required", out _).Should().BeFalse();
    }

    [Fact]
    public void Generate_DescriptionAttribute_IncludesDescription()
    {
        var schema = SchemaGenerator.Generate<DescribedClass>();
        using var doc = JsonDocument.Parse(schema);
        var root = doc.RootElement;

        root.GetProperty("properties")
            .GetProperty("info")
            .GetProperty("description")
            .GetString()
            .Should().Be("desc");
    }

    [Fact]
    public void Generate_JsonPropertyName_UsesCustomName()
    {
        var schema = SchemaGenerator.Generate<CustomNameClass>();
        using var doc = JsonDocument.Parse(schema);
        var root = doc.RootElement;

        root.GetProperty("properties").TryGetProperty("foo", out _).Should().BeTrue();
    }

    [Fact]
    public void Generate_BoolProperty_TypeIsBoolean()
    {
        var schema = SchemaGenerator.Generate<BoolClass>();
        using var doc = JsonDocument.Parse(schema);
        var root = doc.RootElement;

        root.GetProperty("properties")
            .GetProperty("flag")
            .GetProperty("type")
            .GetString()
            .Should().Be("boolean");
    }

    [Fact]
    public void Generate_IntProperty_TypeIsInteger()
    {
        var schema = SchemaGenerator.Generate<IntClass>();
        using var doc = JsonDocument.Parse(schema);
        var root = doc.RootElement;

        root.GetProperty("properties")
            .GetProperty("count")
            .GetProperty("type")
            .GetString()
            .Should().Be("integer");
    }

    [Fact]
    public void Generate_CamelCasesPropertyNames()
    {
        var schema = SchemaGenerator.Generate<CamelCaseClass>();
        using var doc = JsonDocument.Parse(schema);
        var root = doc.RootElement;

        root.GetProperty("properties").TryGetProperty("query", out _).Should().BeTrue();
    }

    // --- Test helper classes ---

    private class EmptyClass { }

    private class RequiredStringClass
    {
        public string Name { get; set; } = "";
    }

    private class NullableStringClass
    {
        public string? Name { get; set; }
    }

    private class DescribedClass
    {
        [Description("desc")]
        public string Info { get; set; } = "";
    }

    private class CustomNameClass
    {
        [JsonPropertyName("foo")]
        public string Bar { get; set; } = "";
    }

    private class BoolClass
    {
        public bool Flag { get; set; }
    }

    private class IntClass
    {
        public int Count { get; set; }
    }

    private class CamelCaseClass
    {
        public string Query { get; set; } = "";
    }
}
