using ClosedXML.Excel;

namespace XlsxWriters;

/// <summary>Вариант A: копия текущей логики продукта на ClosedXML (вся книга в памяти).</summary>
public sealed class ClosedXmlWriter : IXlsxWriter
{
    public string Name => "ClosedXML";

    public void Write(IStageReader reader, string stagePath, IReadOnlyList<StageColumn> columns, string targetPath)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(StageValues.SheetName("Spike"));
        for (var c = 0; c < columns.Count; c++)
        {
            worksheet.Cell(1, c + 1).Value = columns[c].Title;
            worksheet.Cell(1, c + 1).Style.Font.Bold = true;
        }

        var row = 2;
        foreach (var fields in reader.Read(stagePath))
        {
            for (var c = 0; c < columns.Count; c++)
            {
                var field = fields[c];
                if (StageValues.IsNull(field)) continue;
                var cell = worksheet.Cell(row, c + 1);
                cell.Value = ToCellValue(field, columns[c].Type);
                if (!string.IsNullOrWhiteSpace(columns[c].Format)) cell.Style.NumberFormat.Format = columns[c].Format;
            }
            row++;
        }

        worksheet.SheetView.FreezeRows(1);
        worksheet.Range(1, 1, Math.Max(1, row - 1), columns.Count).SetAutoFilter();
        workbook.SaveAs(targetPath);
    }

    private static XLCellValue ToCellValue(string value, StageType type) => type switch
    {
        StageType.Int => long.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
        StageType.Decimal => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
        StageType.Bool => bool.TryParse(value, out var flag) ? flag : value,
        StageType.Date or StageType.DateTime => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
        _ => StageValues.Sanitize(value)
    };
}