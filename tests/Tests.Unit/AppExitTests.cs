using System.Data;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;

namespace Tests.Unit;

[Trait("Category", "T1")]
[Collection("temp-stage")]
public sealed class AppExitTests
{
    [Fact]
    public async Task Ea01_DisposeRemovesResultAndListStagesAndIsIdempotent()
    {
        var viewModel = new MainWindowViewModel(new TwoTabCatalog(), new StubQuery(), new StubExport())
        {
            MinimumLoadingTime = TimeSpan.Zero,
            ClipboardServiceFactory = () => new StubClipboard("7707083893")
        };
        var monthly = viewModel.OpenReport(viewModel.Catalog[0].Children[0].Report!);
        var list = viewModel.OpenReport(viewModel.Catalog[0].Children[1].Report!);

        var resultStage = new TemporaryCsvStage();
        using (var writer = resultStage.OpenWriter()) writer.WriteLine("amount");
        var table = new DataTable();
        table.Columns.Add("amount", typeof(decimal));
        table.Rows.Add(1m);
        monthly.Apply(new ReportQueryResult(new ReportPreview(table, 1), resultStage, 1));

        var listInput = list.ListInput!;
        await listInput.PasteCommand.ExecuteAsync(null);
        var inputStage = listInput.Stage!;

        Assert.True(File.Exists(resultStage.Path));
        Assert.True(File.Exists(inputStage.Path));

        viewModel.Dispose();
        viewModel.Dispose();

        Assert.False(File.Exists(resultStage.Path));
        Assert.False(File.Exists(inputStage.Path));
    }

    private sealed class TwoTabCatalog : IReportCatalogService
    {
        public ReportCatalog Load() => new("1.0",
            [
                Definition("sales.monthly", "Продажи за период", withInput: false),
                Definition("sales.by_inn", "Организации по списку", withInput: true)
            ], [], ["sales"]);

        private static ReportDefinition Definition(string code, string title, bool withInput) => new()
        {
            Code = code,
            Title = title,
            Routine = new RoutineDefinition { Name = "reports.fn_test" },
            Parameters = [],
            Input = withInput
                ? new InputDefinition
                {
                    RowIdParameter = "p_row_id",
                    ResultKey = "row_id",
                    Columns = [new InputColumnDefinition { Name = "p_inn", Type = "string" }]
                }
                : null,
            Result = new ResultDefinition
            {
                Columns = new Dictionary<string, ColumnDefinition> { ["amount"] = new() { Title = "Сумма" } }
            }
        };
    }

    private sealed class StubClipboard(string text) : IClipboardService
    {
        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(text);
    }

    private sealed class StubQuery : IReportQueryService
    {
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report,
            IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            var table = new DataTable();
            table.Columns.Add("amount", typeof(decimal));
            table.Rows.Add(1m);
            return Task.FromResult(new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1));
        }
    }

    private sealed class StubExport : IReportExportService
    {
        public ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath) =>
            new(targetPath, "CSV", result.RowCount, false);
    }
}
