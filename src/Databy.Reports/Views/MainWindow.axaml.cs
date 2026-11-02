using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Databy.Reports.Catalog;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;

namespace Databy.Reports.Views;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _subscribed;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ResultsGrid.PropertyChanged += OnGridPropertyChanged;
        ResultsGrid.LayoutUpdated += (_, _) => { if (ResultsGrid.Columns.Count == 0 && ResultsGrid.ItemsSource is not null) BuildColumns(); };
    }

    public MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    public int LoadedReportCount => ViewModel?.Tabs.Count ?? 0;
    public bool HasResultsTable => ResultsGrid is not null;
    public DataGrid ResultsGrid => this.FindControl<DataGrid>("resultsGrid") ?? throw new InvalidOperationException("DataGrid not found");
    public bool IsLoading => ViewModel?.IsBusy ?? false;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribed is not null) _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribed = ViewModel;
        if (_subscribed is null) return;
        _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        _subscribed.ExportPathProvider = PickExportPathAsync;
    }

    private async Task<string?> PickExportPathAsync(ReportTabViewModel tab)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        return await new AvaloniaFileDialogService(top).PickExportPathAsync("report.xlsx");
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SelectedTab) or nameof(MainWindowViewModel.HasData) or nameof(ReportTabViewModel.Rows))
            BuildColumns();
    }

    private void OnGridPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ItemsControl.ItemsSourceProperty) BuildColumns();
    }

    private void BuildColumns()
    {
        var columns = ViewModel?.SelectedTab?.Definition.Result?.Columns;
        ResultsGrid.Columns.Clear();
        var names = ResultsGrid.ItemsSource switch
        {
            System.Data.DataView view => view.Table?.Columns.Cast<System.Data.DataColumn>().Select(column => column.ColumnName).ToArray(),
            System.Collections.IEnumerable items => items.Cast<object>().FirstOrDefault()?.GetType().GetProperties().Select(property => property.Name).ToArray(),
            _ => null
        };
        if (names is null) return;
        foreach (var name in names)
        {
            var definition = columns is not null && columns.TryGetValue(name, out var found) ? found : null;
            if (definition?.Hidden == true) continue;
            ResultsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = string.IsNullOrWhiteSpace(definition?.Title) ? name : definition!.Title,
                Binding = new Avalonia.Data.Binding(name)
                {
                    StringFormat = string.IsNullOrWhiteSpace(definition?.Format) ? null : "{0:" + definition!.Format + "}"
                }
            });
        }
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TreeView { SelectedItem: CatalogNode { Report: { } report } } || ViewModel is not { } viewModel) return;
        viewModel.OpenReport(report);
    }
}