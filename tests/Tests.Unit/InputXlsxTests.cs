using System.Globalization;
using ClosedXML.Excel;
using Databy.Reports.Catalog;
using Databy.Reports.Input;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class InputXlsxTests
{
    [Fact]
    public void Ix01_IntegerInnIsWrittenWithoutScienceOrFraction()
    {
        using var files = new TempDirectory();
        var path = Path.Combine(files.Path, "inn.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Лист");
            sheet.Cell(1, 1).Value = "ИНН";
            sheet.Cell(1, 2).Value = "Сумма";
            sheet.Cell(2, 1).Value = 7707083893d;
            sheet.Cell(2, 2).Value = 1234.50d;
            sheet.Cell(3, 1).Value = 500100732259d;
            sheet.Cell(3, 2).Value = 0.25d;
            workbook.SaveAs(path);
        }

        using var result = new ListInputParser().ParseFile(path, new InputParseOptions { HasHeader = true });
        var rows = result.Stage.ReadRows().ToArray();

        Assert.Equal(2, rows.Length);
        Assert.Equal("7707083893", rows[0].Cells[0]);
        Assert.Equal("1234.5", rows[0].Cells[1]);
        Assert.Equal("500100732259", rows[1].Cells[0]);
        Assert.Equal("0.25", rows[1].Cells[1]);
    }

    [Fact]
    public void Ix02_DateCellsAreWrittenAsIsoAndDateTypeAccepted()
    {
        using var files = new TempDirectory();
        var path = Path.Combine(files.Path, "dates.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Лист");
            sheet.Cell(1, 1).Value = "p_date";
            sheet.Cell(2, 1).Value = new DateTime(2026, 3, 5);
            sheet.Cell(3, 1).Value = new DateTime(2026, 3, 6, 14, 30, 0);
            workbook.SaveAs(path);
        }

        using var result = new ListInputParser().ParseFile(path, new InputParseOptions
        {
            HasHeader = true,
            Columns = [new InputColumnDefinition { Name = "p_date", Type = "date" }],
            ColumnMapping = new Dictionary<string, int> { ["p_date"] = 0 }
        });
        var rows = result.Stage.ReadRows().ToArray();

        Assert.Equal("2026-03-05", rows[0].Cells[0]);
        // Дата со временем неразборчива как `date` — строка остаётся в stage с ошибкой.
        Assert.Equal("2026-03-06 14:30:00", rows[1].Cells[0]);
        Assert.Equal(1, result.ValidRows);
        Assert.Equal(1, result.ErrorRows);
        Assert.Equal("p_date", Assert.Single(result.Errors).Column);
    }

    [Fact]
    public void Ix03_MultipleColumnsHeaderAndEmptyRows()
    {
        using var files = new TempDirectory();
        var path = Path.Combine(files.Path, "multi.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Лист");
            sheet.Cell(1, 1).Value = "ИНН";
            sheet.Cell(1, 2).Value = "ИНН";
            sheet.Cell(1, 3).Value = "Название";
            sheet.Cell(2, 1).Value = "111";
            sheet.Cell(2, 2).Value = "222";
            sheet.Cell(2, 3).Value = "Ромашка";
            workbook.SaveAs(path);
        }

        using var result = new ListInputParser().ParseFile(path, new InputParseOptions { HasHeader = true });

        Assert.Equal(["ИНН", "ИНН (2)", "Название"], result.SourceColumns);
        Assert.Equal(1, result.TotalRows);
        var row = Assert.Single(result.Stage.ReadRows());
        Assert.Equal(["111", "222", "Ромашка"], row.Cells);
    }

    [Fact]
    public void Ix04_BooleansAndNegativeNumbers()
    {
        using var files = new TempDirectory();
        var path = Path.Combine(files.Path, "bool.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Лист");
            sheet.Cell(1, 1).Value = "flag";
            sheet.Cell(1, 2).Value = "n";
            sheet.Cell(2, 1).Value = true;
            sheet.Cell(2, 2).Value = -42d;
            workbook.SaveAs(path);
        }

        using var result = new ListInputParser().ParseFile(path, new InputParseOptions { HasHeader = true });
        var row = Assert.Single(result.Stage.ReadRows());

        Assert.Equal("true", row.Cells[0]);
        Assert.Equal("-42", row.Cells[1]);
    }
}
