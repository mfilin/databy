using Databy.Reports.Catalog;
using Databy.Reports.Services;
using NpgsqlTypes;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class RoutineCommandTests
{
    [Fact]
    public void Cmd01_BuildsNamedArgumentsInCatalogOrderWithTypes()
    {
        var report = Report(
            new ParameterDefinition { Name = "p_from", Type = "date", Required = true },
            new ParameterDefinition { Name = "p_count", Type = "int", Required = true },
            new ParameterDefinition { Name = "p_amount", Type = "decimal" },
            new ParameterDefinition { Name = "p_text", Type = "string" },
            new ParameterDefinition { Name = "p_flag", Type = "bool" });

        var command = PostgresReportQueryService.BuildCommand(report, new Dictionary<string, object?>
        {
            ["p_from"] = new DateOnly(2026, 1, 1),
            ["p_count"] = 5,
            ["p_amount"] = 10.5m,
            ["p_text"] = "abc",
            ["p_flag"] = true
        });

        Assert.Equal("SELECT * FROM reports.fn_test(p_from => @p_from, p_count => @p_count, p_amount => @p_amount, p_text => @p_text, p_flag => @p_flag)",
            command.Text);
        Assert.Equal(new[] { "p_from", "p_count", "p_amount", "p_text", "p_flag" }, command.Parameters.Select(p => p.Name));
        Assert.Equal(new[]
        {
            NpgsqlDbType.Date, NpgsqlDbType.Integer, NpgsqlDbType.Numeric, NpgsqlDbType.Text, NpgsqlDbType.Boolean
        }, command.Parameters.Select(p => p.NpgsqlType));
    }

    [Fact]
    public void Cmd02_OptionalMissingValueBecomesDbNull()
    {
        var report = Report(
            new ParameterDefinition { Name = "p_from", Type = "date", Required = true },
            new ParameterDefinition { Name = "p_text", Type = "string" });

        var command = PostgresReportQueryService.BuildCommand(report, new Dictionary<string, object?>
        {
            ["p_from"] = new DateOnly(2026, 1, 1)
        });

        Assert.Null(command.Parameters.Single(p => p.Name == "p_text").Value);
    }

    [Fact]
    public void Cmd03_RequiredMissingValueThrowsWithParameterName()
    {
        var report = Report(new ParameterDefinition { Name = "p_from", Type = "date", Required = true });
        var ex = Assert.Throws<ArgumentException>(() => PostgresReportQueryService.BuildCommand(report, new Dictionary<string, object?>()));
        Assert.Equal("p_from", ex.ParamName);
        Assert.Contains("p_from", ex.Message);
    }

    [Fact]
    public void Cmd04_SqlLookingValuesNeverReachCommandText()
    {
        var report = Report(
            new ParameterDefinition { Name = "p_from", Type = "date", Required = true },
            new ParameterDefinition { Name = "p_text", Type = "string", Required = true });
        const string injected = "x'); DROP TABLE items;--";

        var command = PostgresReportQueryService.BuildCommand(report, new Dictionary<string, object?>
        {
            ["p_from"] = new DateOnly(2026, 1, 1),
            ["p_text"] = injected
        });

        Assert.DoesNotContain("DROP TABLE", command.Text);
        Assert.DoesNotContain(injected, command.Text);
        Assert.Equal(injected, command.Parameters.Single(p => p.Name == "p_text").Value);
    }

    [Fact]
    public void Cmd05_UnsupportedTypeIsRejected()
    {
        var report = Report(new ParameterDefinition { Name = "p_lookup", Type = "lookup" });
        Assert.Throws<NotSupportedException>(() => PostgresReportQueryService.BuildCommand(report, new Dictionary<string, object?>()));
    }

    private static ReportDefinition Report(params ParameterDefinition[] parameters) => new()
    {
        Code = "test.report",
        Title = "Тест",
        Routine = new RoutineDefinition { Name = "reports.fn_test", Kind = "function" },
        Parameters = [.. parameters]
    };
}
