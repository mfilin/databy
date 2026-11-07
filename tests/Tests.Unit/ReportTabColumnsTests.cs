using System.Data;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class ReportTabColumnsTests
{
    [Fact]
    public void Tab01_WithoutResultAllDatabaseColumnsAppear()
    {
        using var viewModel = Create(result: null);
        var tab = viewModel.OpenReport(viewModel.Catalog[0].Children[0].Report!);
        tab.Apply(Result());

        Assert.Equal(["ОГРН", "ИНН", "Наименование организации", "name_", "name_full_"],
            tab.TableColumns.Select(column => column.Name));
        Assert.Equal("Наименование организации",
            tab.TableColumns.Single(column => column.Name == "Наименование организации").Title);
    }

    [Fact]
    public void Tab02_HideRemovesColumnFromTable()
    {
        using var viewModel = Create(new ResultDefinition { Hide = ["ОГРН"] });
        var tab = viewModel.OpenReport(viewModel.Catalog[0].Children[0].Report!);
        tab.Apply(Result());

        Assert.Equal(["ИНН", "Наименование организации", "name_", "name_full_"],
            tab.TableColumns.Select(column => column.Name));
    }

    [Fact]
    public async Task Tab03_AllColumnsHiddenProducesStatusError()
    {
        using var viewModel = Create(new ResultDefinition
        {
            Hide = ["ОГРН", "ИНН", "Наименование организации", "name_", "name_full_"]
        });
        viewModel.OpenReport(viewModel.Catalog[0].Children[0].Report!);

        await viewModel.RunCommand.ExecuteAsync(null);

        Assert.Contains("Все колонки скрыты", viewModel.Status);
        Assert.False(viewModel.HasData);
        Assert.Null(viewModel.SelectedTab!.CurrentResult);
    }

    private static ReportQueryResult Result()
    {
        var table = new DataTable();
        foreach (var name in new[] { "ОГРН", "ИНН", "Наименование организации", "name_", "name_full_" })
            table.Columns.Add(name, typeof(string));
        table.Rows.Add("1", "2", "3", "4", "5");
        return new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1);
    }

    private static MainWindowViewModel Create(ResultDefinition? result)
    {
        var definition = new ReportDefinition
        {
            Code = "test.report",
            Title = "Отчёт",
            Routine = new RoutineDefinition { Name = "reports.fn_test" },
            Parameters = [],
            Result = result
        };
        return new MainWindowViewModel(new StubCatalog(definition), new StubQuery(), new StubExport())
        {
            MinimumLoadingTime = TimeSpan.Zero
        };
    }

    private sealed class StubCatalog(ReportDefinition definition) : IReportCatalogService
    {
        public ReportCatalog Load() => new("1", [definition], [], ["test"]);
    }

    private sealed class StubQuery : IReportQueryService
    {
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
            => Task.FromResult(Result());
    }

    private sealed class StubExport : IReportExportService
    {
        public ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath) =>
            new(targetPath, "CSV", result.RowCount, false);
    }
}
