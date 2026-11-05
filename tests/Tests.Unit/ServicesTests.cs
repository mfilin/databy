using System.Data;
using System.Text;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class MainWindowViewModelTests
{


    [Fact]
    public void Vm01_BuildsCatalogTreeWithoutAutoOpeningReport()
    {
        var viewModel = CreateViewModel();
        try
        {
            var folder = Assert.Single(viewModel.Catalog);
            Assert.Equal("sales", folder.Title);
            Assert.True(folder.IsFolder);
            var reportNode = Assert.Single(folder.Children);
            Assert.False(reportNode.IsFolder);
            Assert.NotNull(reportNode.Report);

            Assert.Null(viewModel.SelectedTab);
            Assert.Empty(viewModel.Tabs);
            Assert.False(viewModel.IsReportSelected);
            Assert.Equal("2.0", viewModel.CatalogVersion);
            Assert.False(viewModel.CanExport);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public void Vm02_OpenReportReusesExistingTab()
    {
        var viewModel = CreateViewModel();
        try
        {
            var definition = FirstReport(viewModel);
            var first = viewModel.OpenReport(definition);
            Assert.True(viewModel.IsReportSelected);
            var second = viewModel.OpenReport(definition);
            Assert.Same(first, second);
            Assert.Single(viewModel.Tabs);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public async Task Vm03_RunFillsRowsAndEnablesPaging()
    {
        var viewModel = CreateViewModel();
        try
        {
            viewModel.OpenReport(FirstReport(viewModel));
            await viewModel.RunCommand.ExecuteAsync(null);
            Assert.True(viewModel.HasData);
            Assert.False(viewModel.CanGoForward);
            Assert.False(viewModel.CanGoBack);
            Assert.Equal("1 из 1", viewModel.PageCaption);
            Assert.True(viewModel.CanExport);
            Assert.Equal(2, viewModel.SelectedTab!.PreviewRows);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public async Task Vm04_PaginationSlicesPreviewByPageSize()
    {
        var viewModel = CreateViewModel(rowCount: MainWindowViewModel.PageSize * 2 + 5);
        try
        {
            viewModel.OpenReport(FirstReport(viewModel));
            await viewModel.RunCommand.ExecuteAsync(null);
            Assert.Equal(3, viewModel.PageCount);
            Assert.True(viewModel.CanGoForward);

            viewModel.NextPageCommand.Execute(null);
            Assert.Equal(1, viewModel.Page);
            Assert.True(viewModel.CanGoBack);

            viewModel.LastPageCommand.Execute(null);
            Assert.Equal(2, viewModel.Page);
            Assert.Equal(5, viewModel.SelectedTab!.Rows!.Count);
            Assert.False(viewModel.CanGoForward);

            viewModel.FirstPageCommand.Execute(null);
            Assert.Equal(0, viewModel.Page);
            Assert.Equal(MainWindowViewModel.PageSize, viewModel.SelectedTab!.Rows!.Count);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public async Task Vm05_InvalidPeriodProducesErrorStatus()
    {
        var viewModel = CreateViewModel();
        try
        {
            viewModel.OpenReport(FirstReport(viewModel));
            viewModel.SetPeriod(new DateTimeOffset(new DateTime(2026, 5, 10)), new DateTimeOffset(new DateTime(2026, 5, 1)));
            await viewModel.RunCommand.ExecuteAsync(null);
            Assert.Contains("не может быть раньше", viewModel.Status);
            Assert.False(viewModel.HasData);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public void Vm06_CatalogFoldersFromYamlAreUsedForTree()
    {
        var viewModel = CreateViewModel();
        try
        {
            Assert.Equal("sales", Assert.Single(viewModel.Catalog).Title);
            Assert.Equal("sales.monthly", viewModel.Catalog[0].Children[0].ReportCode);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public void Vm07_CloseTabSelectsNeighbourAndClearsSelectionWhenEmpty()
    {
        var viewModel = CreateViewModel();
        try
        {
            var definition = FirstReport(viewModel);
            var first = viewModel.OpenReport(definition);
            var second = viewModel.OpenReport(new ReportDefinition
            {
                Code = "leaders.sales",
                Title = "Лидеры продаж",
                Routine = new RoutineDefinition { Name = "reports.fn_sales_leaders" },
                Parameters = definition.Parameters,
                Result = new ResultDefinition()
            });
            Assert.Equal(2, viewModel.Tabs.Count);

            viewModel.CloseTab(second);
            Assert.Same(first, viewModel.SelectedTab);
            Assert.True(viewModel.IsReportSelected);

            viewModel.CloseTab(first);
            Assert.Empty(viewModel.Tabs);
            Assert.Null(viewModel.SelectedTab);
            Assert.False(viewModel.IsReportSelected);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public void Vm08_CloseOtherTabsKeepsOnlyGivenTab()
    {
        var viewModel = CreateViewModel();
        try
        {
            var definition = FirstReport(viewModel);
            var first = viewModel.OpenReport(definition);
            viewModel.OpenReport(new ReportDefinition
            {
                Code = "leaders.sales",
                Title = "Лидеры продаж",
                Routine = new RoutineDefinition { Name = "reports.fn_sales_leaders" },
                Parameters = definition.Parameters,
                Result = new ResultDefinition()
            });
            viewModel.OpenReport(new ReportDefinition
            {
                Code = "leaders.city",
                Title = "Лидеры города-продаж",
                Routine = new RoutineDefinition { Name = "reports.fn_city_sales_leaders" },
                Parameters = definition.Parameters,
                Result = new ResultDefinition()
            });
            Assert.Equal(3, viewModel.Tabs.Count);

            viewModel.CloseOtherTabs(first);
            Assert.Same(first, Assert.Single(viewModel.Tabs));
            Assert.Same(first, viewModel.SelectedTab);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public async Task Vm09_LoadingPanelHonoursMinimumLoadingTime()
    {
        var viewModel = new MainWindowViewModel(new StubCatalog(), new StubQuery(0), new StubExport())
        {
            MinimumLoadingTime = TimeSpan.FromMilliseconds(200)
        };
        try
        {
            viewModel.OpenReport(FirstReport(viewModel));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await viewModel.RunCommand.ExecuteAsync(null);
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(190), $"Загрузка заняла {stopwatch.ElapsedMilliseconds} мс");
            Assert.False(viewModel.IsBusy);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public async Task Vm10_CancelDuringLoadingReleasesStagedFile()
    {
        TemporaryCsvStage? stage = null;
        var query = new CancelableQuery(() => stage = new TemporaryCsvStage());
        var viewModel = new MainWindowViewModel(new StubCatalog(), query, new StubExport())
        {
            MinimumLoadingTime = TimeSpan.FromSeconds(30)
        };
        try
        {
            var tab = viewModel.OpenReport(FirstReport(viewModel));
            var run = viewModel.RunCommand.ExecuteAsync(null);
            await query.ResultProduced.Task.WaitAsync(TimeSpan.FromSeconds(10));

            viewModel.CancelCommand.Execute(null);
            await run.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(stage);
            Assert.False(File.Exists(stage!.Path), "Временный stage-файл должен быть удалён после отмены");
            Assert.Equal("Запуск отменён", viewModel.Status);
            Assert.Null(tab.CurrentResult);
            Assert.False(viewModel.HasData);
        }
        finally { viewModel.Dispose(); }
    }

    internal static ReportDefinition FirstReport(MainWindowViewModel viewModel) =>
        viewModel.Catalog.SelectMany(folder => folder.Children).First(node => node.Report is not null).Report!;

    internal static MainWindowViewModel CreateViewModel(int rowCount = 2) =>
        new(new StubCatalog(), new StubQuery(rowCount), new StubExport())
        {
            MinimumLoadingTime = TimeSpan.Zero
        };

    private sealed class StubCatalog : IReportCatalogService
    {
        public ReportCatalog Load() => new("2.0",
            [new ReportDefinition
            {
                Code = "sales.monthly",
                Title = "Продажи за период",
                Routine = new RoutineDefinition { Name = "reports.fn_sales" },
                Parameters = [new ParameterDefinition { Name = "p_from", Type = "date" }, new ParameterDefinition { Name = "p_to", Type = "date" }],
                Result = new ResultDefinition { Columns = new Dictionary<string, ColumnDefinition> { ["amount"] = new() { Title = "Сумма" } } }
            }],
            [], ["sales"]);
    }

    private sealed class StubQuery(int rowCount) : IReportQueryService
    {
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            var table = new DataTable();
            table.Columns.Add("amount", typeof(decimal));
            for (var i = 0; i < rowCount; i++) table.Rows.Add(i + 0.5m);
            return Task.FromResult(new ReportQueryResult(new ReportPreview(table, rowCount), new TemporaryCsvStage(), rowCount));
        }
    }

    private sealed class CancelableQuery(Func<TemporaryCsvStage> createStage) : IReportQueryService
    {
        public TaskCompletionSource ResultProduced { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            var table = new DataTable();
            table.Columns.Add("amount", typeof(decimal));
            table.Rows.Add(1m);
            var stage = createStage();
            using (var writer = stage.OpenWriter())
                ReportExportWriter.WriteCsvRecord(writer, new[] { "amount" });
            ResultProduced.TrySetResult();
            return Task.FromResult(new ReportQueryResult(new ReportPreview(table, 1), stage, 1));
        }
    }

    private sealed class StubExport : IReportExportService
    {
        public ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath) =>
            new(targetPath, "CSV", result.RowCount, false);
    }
}

[Trait("Category", "T1")]
public sealed class ApplicationSettingsTests
{
    [Fact]
    public void Set01_ThrowsClearErrorWhenNoConnectionIsConfigured()
    {
        using var dir = new TempDirectory();
        using var env = new EnvironmentVariableScope(ApplicationSettings.ConnectionVariable, null);

        var settings = new ApplicationSettings(dir.Path);

        Assert.True(File.Exists(Path.Combine(dir.Path, "logs")) == false);
        var ex = Assert.Throws<InvalidOperationException>(() => settings.ConnectionString);
        Assert.Contains(ApplicationSettings.ConnectionVariable, ex.Message);
        Assert.Contains("appsettings.local.json", ex.Message);
    }

    [Fact]
    public void Set02_ReadsConnectionStringFromLocalFile()
    {
        using var dir = new TempDirectory();
        using var env = new EnvironmentVariableScope(ApplicationSettings.ConnectionVariable, null);
        File.WriteAllText(Path.Combine(dir.Path, "appsettings.local.json"),
            "{\"ConnectionString\": \"Host=db.example;Database=reports\"}", new UTF8Encoding(false));

        var settings = new ApplicationSettings(dir.Path);

        Assert.Equal("Host=db.example;Database=reports", settings.ConnectionString);
    }

    [Fact]
    public void Set03_EnvironmentVariableWinsOverFile()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "appsettings.local.json"),
            "{\"ConnectionString\": \"Host=file;Database=x\"}", new UTF8Encoding(false));
        using var env = new EnvironmentVariableScope(ApplicationSettings.ConnectionVariable, "Host=env;Database=y");

        var settings = new ApplicationSettings(dir.Path);

        Assert.Equal("Host=env;Database=y", settings.ConnectionString);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
