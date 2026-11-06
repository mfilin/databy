using Databy.Reports.Catalog;

namespace Databy.Reports.ViewModels;

public sealed class ReportColumnViewModel
{
    public ReportColumnViewModel(string name, ColumnDefinition definition)
    {
        Name = name;
        Title = string.IsNullOrWhiteSpace(definition.Title) ? name : definition.Title;
        Format = definition.Format;
        Hidden = definition.Hidden;
    }

    public string Name { get; }
    public string Title { get; }
    public string? Format { get; }
    public bool Hidden { get; }
}
