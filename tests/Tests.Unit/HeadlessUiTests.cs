using Avalonia.Headless.XUnit;
using Databy.Reports;
using System.Data;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class HeadlessUiTests
{
    [AvaloniaFact]
    public void Ui04_HeadlessWindowLoadsCatalogAndResultsGrid()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            Assert.IsType<Avalonia.Controls.DockPanel>(window.Content);
            Assert.True(window.LoadedReportCount > 0);
            Assert.True(window.HasResultsTable);
            Assert.True(window.HasLoadingBar);
            Assert.False(window.IsLoading);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Ui_NumericColumnFormatKeepsValuesVisible()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var table = new DataTable();
            table.Columns.Add("amount", typeof(decimal));
            table.Rows.Add(1234.5m);
            window.ResultsGrid.ItemsSource = table.DefaultView;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var column = Assert.IsType<Avalonia.Controls.DataGridTextColumn>(Assert.Single(window.ResultsGrid.Columns));
            Assert.Equal("{0:#,##0.00}", Assert.IsType<Avalonia.Data.Binding>(column.Binding).StringFormat);
        }
        finally { window.Close(); }
    }
}
