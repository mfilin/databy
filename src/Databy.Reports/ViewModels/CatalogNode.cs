using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Databy.Reports.Catalog;

namespace Databy.Reports.ViewModels;

public sealed partial class CatalogNode : ObservableObject
{
    public CatalogNode(string title, string? reportCode = null)
    {
        Title = title;
        ReportCode = reportCode;
    }

    public string Title { get; }
    public string? ReportCode { get; }
    public ObservableCollection<CatalogNode> Children { get; } = [];
    public ReportDefinition? Report { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded = true;

    public bool IsFolder => Report is null;
    public string Icon => IsFolder ? "📁" : "📄";

    public IEnumerable<CatalogNode> Flatten()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var node in child.Flatten())
                yield return node;
    }
}
