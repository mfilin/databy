using System.Globalization;
using MiniExcelLibs;
using MiniExcelLibs.OpenXml;

namespace XlsxWriters;

/// <summary>Вариант B: MiniExcel в потоковом режиме.</summary>
public sealed class MiniExcelWriter : IXlsxWriter
{
    public string Name => "MiniExcel";

    public void Write(IStageReader reader, string stagePath, IReadOnlyList<StageColumn> columns, string targetPath)
    {
        if (File.Exists(targetPath)) File.Delete(targetPath);

        var config = new OpenXmlConfiguration
        {
            FastMode = true,
            FreezeRowCount = 1,
            AutoFilter = true
        };

        var rows = BuildRows(reader, stagePath, columns);
        MiniExcel.SaveAs(targetPath, rows, excelType: ExcelType.XLSX, configuration: config,
            sheetName: StageValues.SheetName("Spike"));
    }

    private static IEnumerable<IDictionary<string, object?>> BuildRows(IStageReader reader, string stagePath, IReadOnlyList<StageColumn> columns)
    {
        foreach (var fields in reader.Read(stagePath))
        {
            var row = new Dictionary<string, object?>(columns.Count, StringComparer.Ordinal);
            for (var c = 0; c < columns.Count; c++)
            {
                var field = fields[c];
                row[columns[c].Title] = StageValues.IsNull(field) ? null : ToValue(field, columns[c].Type);
            }
            yield return row;
        }
    }

    private static object ToValue(string value, StageType type) => type switch
    {
        StageType.Int => long.Parse(value, CultureInfo.InvariantCulture),
        StageType.Decimal => double.Parse(value, CultureInfo.InvariantCulture),
        StageType.Bool => bool.TryParse(value, out var flag) ? flag : value,
        StageType.Date or StageType.DateTime => DateTime.Parse(value, CultureInfo.InvariantCulture),
        _ => StageValues.Sanitize(value)
    };
}