using Databy.Reports.Catalog;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class ParameterDefaultsTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Theory]
    [InlineData("date", "2026-01-31", typeof(DateOnly))]
    [InlineData("int", "5", typeof(int))]
    [InlineData("decimal", "12.75", typeof(decimal))]
    [InlineData("bool", "TrUe", typeof(bool))]
    [InlineData("string", "  abc  ", typeof(string))]
    public void Pd01_ResolvesTypedValuePerType(string type, string defaultValue, Type expectedType)
    {
        var resolved = ParameterDefaults.Resolve(Parameter(type, defaultValue), Today);
        Assert.NotNull(resolved);
        Assert.Equal(expectedType, resolved!.GetType());
    }

    [Fact]
    public void Pd02_ResolvesExpectedValues()
    {
        Assert.Equal(new DateOnly(2026, 11, 5), ParameterDefaults.Resolve(Parameter("date", "today"), Today));
        Assert.Equal(new DateOnly(2026, 11, 1), ParameterDefaults.Resolve(Parameter("date", "month_start"), Today));
        Assert.Equal(5, ParameterDefaults.Resolve(Parameter("int", "5"), Today));
        Assert.Equal(12.75m, ParameterDefaults.Resolve(Parameter("decimal", "12.75"), Today));
        Assert.True((bool)ParameterDefaults.Resolve(Parameter("bool", "true"), Today)!);
        Assert.Equal("  abc  ", ParameterDefaults.Resolve(Parameter("string", "  abc  "), Today));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Pd03_EmptyOrWhitespaceDefaultIsNull(string defaultValue)
    {
        Assert.Null(ParameterDefaults.Resolve(Parameter("int", defaultValue), Today));
    }

    [Fact]
    public void Pd04_NoDefaultIsNull()
    {
        Assert.Null(ParameterDefaults.Resolve(new ParameterDefinition { Name = "p_limit", Type = "int" }, Today));
    }

    [Theory]
    [InlineData("int", "abc")]
    [InlineData("decimal", "1,5")]
    [InlineData("bool", "yes")]
    public void Pd05_InvalidValueThrowsFormatExceptionWithNameAndType(string type, string defaultValue)
    {
        var ex = Assert.Throws<FormatException>(() => ParameterDefaults.Resolve(Parameter(type, defaultValue), Today));
        Assert.Contains("p_value", ex.Message);
        Assert.Contains(type, ex.Message);
    }

    private static ParameterDefinition Parameter(string type, string defaultValue) =>
        new() { Name = "p_value", Type = type, Default = defaultValue };
}