using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;

namespace XlsxWriters;

/// <summary>Вариант C: DocumentFormat.OpenXml с OpenXmlWriter — лист пишется потоково.</summary>
public sealed class OpenXmlStreamWriter : IXlsxWriter
{
    private const uint HeaderStyle = 1;
    private const uint DateStyle = 2;
    private const uint NumberStyle = 3;

    public string Name => "OpenXml";

    public void Write(IStageReader reader, string stagePath, IReadOnlyList<StageColumn> columns, string targetPath)
    {
        if (File.Exists(targetPath)) File.Delete(targetPath);

        using var document = SpreadsheetDocument.Create(targetPath, SpreadsheetDocumentType.Workbook, autoSave: false);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = BuildStylesheet();
        stylesPart.Stylesheet.Save();

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        using (var xmlWriter = OpenXmlWriter.Create(worksheetPart))
        {
            xmlWriter.WriteStartElement(new Worksheet());
            xmlWriter.WriteStartElement(new SheetViews());
            var sheetView = new SheetView { WorkbookViewId = 0 };
sheetView.Append(new Pane
            {
                VerticalSplit = 1D,
                TopLeftCell = "A2",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen
            });
sheetView.Append(new Selection { Pane = PaneValues.BottomLeft, ActiveCell = "A2", SequenceOfReferences = new ListValue<StringValue> { InnerText = "A2" } });
            xmlWriter.WriteElement(sheetView);
            xmlWriter.WriteEndElement();

            xmlWriter.WriteStartElement(new SheetData());
            WriteHeaderRow(xmlWriter, columns);

            var rowIndex = 2u;
            foreach (var fields in reader.Read(stagePath))
            {
                WriteDataRow(xmlWriter, rowIndex++, fields, columns);
            }
            xmlWriter.WriteEndElement();

            if (columns.Count > 0)
            {
                var reference = $"A1:{ColumnLetter(columns.Count)}{Math.Max(1, rowIndex - 1)}";
                xmlWriter.WriteElement(new AutoFilter { Reference = reference });
            }

            xmlWriter.WriteEndElement();
        }

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = StageValues.SheetName("Spike")
        });
        workbookPart.Workbook.Save();
    }

    private static void WriteHeaderRow(OpenXmlWriter writer, IReadOnlyList<StageColumn> columns)
    {
        writer.WriteStartElement(new Row { RowIndex = 1 });
        for (var c = 0; c < columns.Count; c++)
        {
            writer.WriteStartElement(new Cell
            {
                CellReference = ColumnLetter(c + 1) + "1",
                DataType = CellValues.InlineString,
                StyleIndex = HeaderStyle
            });
            writer.WriteElement(new InlineString(new Text(columns[c].Title)));
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteDataRow(OpenXmlWriter writer, uint rowIndex, string[] fields, IReadOnlyList<StageColumn> columns)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });
        for (var c = 0; c < columns.Count; c++)
        {
            var field = fields[c];
            if (StageValues.IsNull(field)) continue;

            var type = columns[c].Type;
            var reference = ColumnLetter(c + 1) + rowIndex.ToString(CultureInfo.InvariantCulture);
            switch (type)
            {
                case StageType.Int:
                case StageType.Decimal:
                {
                    if (!StageValues.ParseNumber(field).IsNumber) { WriteText(writer, reference, field, NumberStyle); break; }
                    writer.WriteStartElement(new Cell { CellReference = reference, DataType = CellValues.Number, StyleIndex = NumberStyle });
                    writer.WriteElement(new CellValue(field));
                    writer.WriteEndElement();
                    break;
                }
                case StageType.Date:
                case StageType.DateTime:
                {
                    if (!StageValues.ParseDate(field).IsDate) { WriteText(writer, reference, field, DateStyle); break; }
                    var serial = StageValues.ParseDate(field).Value.ToOADate().ToString("R", CultureInfo.InvariantCulture);
                    writer.WriteStartElement(new Cell { CellReference = reference, DataType = CellValues.Number, StyleIndex = DateStyle });
                    writer.WriteElement(new CellValue(serial));
                    writer.WriteEndElement();
                    break;
                }
                case StageType.Bool:
                    writer.WriteStartElement(new Cell { CellReference = reference, DataType = CellValues.Boolean });
                    writer.WriteElement(new CellValue(bool.TryParse(field, out var flag) && flag ? "1" : "0"));
                    writer.WriteEndElement();
                    break;
                default:
                    WriteText(writer, reference, StageValues.Sanitize(field), 0);
                    break;
            }
        }
        writer.WriteEndElement();
    }

    private static void WriteText(OpenXmlWriter writer, string reference, string value, uint style)
    {
        writer.WriteStartElement(new Cell { CellReference = reference, DataType = CellValues.InlineString, StyleIndex = style });
        writer.WriteElement(new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }));
        writer.WriteEndElement();
    }

    private static Stylesheet BuildStylesheet()
    {
        var fonts = new Fonts(
            new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }))
        { Count = 2 };

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
        { Count = 2 };

        var borders = new Borders(new Border()) { Count = 1 };

        var cellFormats = new CellFormats(
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0 },
            new CellFormat { FontId = 1, FillId = 0, BorderId = 0, ApplyFont = true },
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 14, ApplyNumberFormat = true },
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 4, ApplyNumberFormat = true })
        { Count = 4 };

        return new Stylesheet(fonts, fills, borders, cellFormats);
    }

    private static string ColumnLetter(int column)
    {
        var letter = "";
        while (column > 0)
        {
            var modulo = (column - 1) % 26;
            letter = (char)('A' + modulo) + letter;
            column = (column - modulo) / 26;
        }
        return letter;
    }
}