using System.Globalization;
using System.Text;
using Databy.Reports.Input;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class ResultMergerTests
{
    private static readonly string[] SourceColumns = ["Колонка 1", "Колонка 2"];
    private static readonly string[] DatabaseColumns = ["inn", "name", "amount"];

    [Fact]
    public void Rm01_NotFoundRowGetsStatusOnly()
    {
        var merger = new ResultMerger(SourceColumns, DatabaseColumns);
        var output = Merge(merger, [Row(1, null, "111", "x")], []);

        Assert.Single(output);
        Assert.Contains("Не найдено", output[0]);
    }

    [Fact]
    public void Rm02_OneToManyRepeatsSourceValues()
    {
        var merger = new ResultMerger(SourceColumns, DatabaseColumns);
        var output = Merge(merger, [Row(1, null, "111", "x")],
        [
            new BatchRow(1, ["7707", "Ромашка", "10.5"]),
            new BatchRow(1, ["7707", "Лютик", "20"])
        ]);

        Assert.Equal(2, output.Length);
        Assert.Contains("111", output[0]);
        Assert.Contains("Ромашка", output[0]);
        Assert.Contains("Лютик", output[1]);
        Assert.All(output, line => Assert.Contains("Найдено", line));
    }

    [Fact]
    public void Rm03_InputErrorKeepsReasonAndCellValues()
    {
        var merger = new ResultMerger(SourceColumns, DatabaseColumns);
        var output = Merge(merger, [Row(1, "не удалось разобрать значение как date", "111", "x")], []);

        Assert.Single(output);
        Assert.Contains("Ошибка во входных данных: не удалось разобрать значение как date", output[0]);
        Assert.Contains("111", output[0]);
    }

    [Fact]
    public void Rm04_OrderFollowsSourceNotDatabase()
    {
        var merger = new ResultMerger(SourceColumns, DatabaseColumns);
        var output = Merge(merger,
            [Row(3, null, "a", "x"), Row(1, null, "b", "y")],
            [new BatchRow(1, ["k1", "n1", "1"]), new BatchRow(3, ["k3", "n3", "3"])]);

        Assert.Contains("x", output[0]);
        Assert.Contains("y", output[1]);
    }

    [Fact]
    public void Rm05_ColumnConflictGetsSuffix()
    {
        var merger = new ResultMerger(["inn"], ["inn"]);

        Assert.Equal(["inn", "inn (БД)"], merger.OutputColumns);
    }

    [Fact]
    public void Rm06_MatchesDetectsDifferentColumnSets()
    {
        var merger = new ResultMerger(SourceColumns, DatabaseColumns);
        Assert.True(merger.Matches(DatabaseColumns));
        Assert.False(merger.Matches(["inn", "name"]));
        Assert.False(merger.Matches(["name", "inn", "amount"]));
    }

    [Fact]
    public void Rm07_HundredThousandRowsAreMergedInBatches()
    {
        var merger = new ResultMerger(SourceColumns, DatabaseColumns);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long written = 0;

        for (var batchStart = 0; batchStart < 100_000; batchStart += 1000)
        {
            var source = new List<InputRow>(1000);
            var database = new List<BatchRow>(500);
            for (var i = 0; i < 1000; i++)
            {
                var id = batchStart + i + 1;
                source.Add(Row(id, null, "v" + id, "x"));
                if (id % 2 == 0) database.Add(new BatchRow(id, ["inn" + id, "name" + id, "1.5"]));
            }

            using var buffer = new StringWriter(CultureInfo.InvariantCulture);
            written += merger.Write(buffer, new BatchResult(source, database));
        }

        stopwatch.Stop();
        Assert.Equal(100_000, written);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Слияние заняло {stopwatch.Elapsed}");
    }

    [Fact]
    public void Rm08_FormatValueIsInvariant()
    {
        Assert.Equal("1.5", ResultMerger.FormatValue(1.5m));
        Assert.Equal("True", ResultMerger.FormatValue(true));
        Assert.Equal("2026-03-05", ResultMerger.FormatValue(new DateOnly(2026, 3, 5)));
        Assert.Equal("", ResultMerger.FormatValue(null));
        Assert.Equal("", ResultMerger.FormatValue(DBNull.Value));
    }

    private static InputRow Row(int id, string? error, params string[] cells) => new(id, error, cells);

    private static string[] Merge(ResultMerger merger, IReadOnlyList<InputRow> source, IReadOnlyList<BatchRow> database)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        merger.Write(writer, new BatchResult(source, database));
        return writer.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }
}
