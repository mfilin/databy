using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Databy.Reports.Catalog;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;

namespace Databy.Reports.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    public int LoadedReportCount => ViewModel?.Tabs.Count ?? 0;
    public bool HasResultsTable => ResultsTable is not null;
    public TableView ResultsTable => this.FindControl<TableView>("resultsTable") ?? throw new InvalidOperationException("TableView not found");
    public bool IsLoading => ViewModel?.IsBusy ?? false;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ViewModel is { } viewModel)
            viewModel.ExportPathProvider = PickExportPathAsync;
    }

    private async Task<string?> PickExportPathAsync(ReportTabViewModel tab)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;
        return await new AvaloniaFileDialogService(top).PickExportPathAsync("report.xlsx");
    }

    private void OnTreeDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is not TreeView { SelectedItem: CatalogNode { Report: { } report } } || ViewModel is not { } viewModel) return;
        e.Handled = true;
        viewModel.OpenReport(report);
    }

    private void OnTabCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: ReportTabViewModel tab })
            ViewModel?.CloseTab(tab);
    }

    private void OnTabDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is not Control { DataContext: ReportTabViewModel tab }) return;
        e.Handled = true;
        ViewModel?.CloseTab(tab);
    }

    private void OnCloseTabMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        CloseFromMenuItem(sender, tab => ViewModel?.CloseTab(tab));

    private void OnCloseOtherTabsMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        CloseFromMenuItem(sender, tab => ViewModel?.CloseOtherTabs(tab));

    private void OnCloseAllTabsMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.CloseAllTabs();

    private static void CloseFromMenuItem(object? sender, Action<ReportTabViewModel> close)
    {
        if (sender is MenuItem { DataContext: ReportTabViewModel tab })
            close(tab);
    }
}
