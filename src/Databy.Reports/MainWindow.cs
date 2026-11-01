using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Npgsql;
using Serilog;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using ReportColumnDefinition = Databy.Reports.Catalog.ColumnDefinition;

namespace Databy.Reports;

public sealed class MainWindow : Window
{
    public int LoadedReportCount => _definitions.Count;
    public bool HasResultsTable => Content is DockPanel root && root.Children.Contains(_grid);
    public DataGrid ResultsGrid => _grid;
    public bool HasLoadingBar => _progress.Parent is StackPanel footer && footer.Children.Contains(_progress);
    public bool IsLoading => _progress.IsVisible;
    private readonly ComboBox _reports = new() { Name = "reportsSelector", Width = 280 };
    private readonly DatePicker _from = new() { Width = 150 };
    private readonly DatePicker _to = new() { Width = 150 };
    private readonly Button _run = new() { Content = "Обновить", MinWidth = 120 };
    private readonly Button _cancel = new() { Content = "Отмена", MinWidth = 100, IsEnabled = false };
    private readonly Button _export = new() { Content = "Экспорт", MinWidth = 100, IsEnabled = false };
    private readonly Button _reload = new() { Content = "Перечитать каталог", MinWidth = 170 };
    private readonly TextBlock _status = new() { Text = "Выберите отчёт и нажмите «Обновить»", VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false, Height = 5, Minimum = 0, Maximum = 100 };
    private readonly DataGrid _grid = new() { Name = "resultsGrid", AutoGenerateColumns = true, IsReadOnly = true, GridLinesVisibility = DataGridGridLinesVisibility.All, CanUserSortColumns = true, UseLogicalScrollable = true };
    private readonly List<ReportDefinition> _definitions = [];
    private DataTable? _lastResult;
    private TemporaryCsvStage? _stagedCsvStage;
    private long _rowCount;
    private CancellationTokenSource? _runCts;
    private Dictionary<string, ReportColumnDefinition> _columnConfig = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _connectionString;
    private const int PreviewLimit = 5000;

    public MainWindow()
    {
        Title = "Databy Reports — отчёты";
        Width = 1180; Height = 760; MinWidth = 850; MinHeight = 550;
        _connectionString = Environment.GetEnvironmentVariable("DATABY_CONNECTION") ?? ReadLocalConnectionString();
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.File("logs/databy-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14).CreateLogger();
        BuildLayout();
        LoadCatalog();
        _run.Click += async (_, _) => await RunReportAsync();
        _cancel.Click += (_, _) => _runCts?.Cancel();
        _reload.Click += (_, _) => LoadCatalog();
        _export.Click += async (_, _) => await ExportAsync();
        Closed += (_, _) => { _stagedCsvStage?.Dispose(); Log.CloseAndFlush(); };
        _grid.AutoGeneratingColumn += (_, e) =>
        {
            if (_columnConfig.TryGetValue(e.PropertyName ?? "", out var config))
            {
                e.Column.Header = config.Title ?? e.PropertyName;
                if (config.Hidden) e.Cancel = true;
                if (e.Column is DataGridTextColumn text && !string.IsNullOrWhiteSpace(config.Format))
                    text.Binding = new Avalonia.Data.Binding(e.PropertyName!) { StringFormat = "{0:" + config.Format + "}" };
            }
            else e.Column.Header = e.PropertyName;
        };
    }

    private static string ReadLocalConnectionString()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");
        if (File.Exists(file))
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            if (json.RootElement.TryGetProperty("ConnectionString", out var cs) && !string.IsNullOrWhiteSpace(cs.GetString())) return cs.GetString()!;
        }
        return "Host=localhost;Port=5432;Database=db1;Username=db1;Timeout=10;Command Timeout=60";
    }

    private void BuildLayout()
    {
        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(18) };
        var top = new StackPanel { Spacing = 12 };
        var title = new TextBlock { Text = "Отчётность", FontSize = 25, FontWeight = FontWeight.SemiBold };
        top.Children.Add(title);
        var row = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Labeled("Отчёт", _reports));
        row.Children.Add(Labeled("Дата с", _from));
        row.Children.Add(Labeled("Дата по", _to));
        row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(4, 20, 0, 0), Children = { _run, _cancel, _export, _reload } });
        top.Children.Add(row);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var footerContent = new StackPanel { Spacing = 6, Children = { _progress, _status } };
        var footer = new Border { BorderBrush = new SolidColorBrush(Color.Parse("#D8DEE8")), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(4, 10), Child = footerContent };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(_grid);
        Content = root;
    }

    private static Control Labeled(string label, Control control) => new StackPanel
    {
        Spacing = 4, Margin = new Thickness(0, 0, 16, 0), Children = { new TextBlock { Text = label, FontSize = 12, Foreground = Brushes.DimGray }, control }
    };

    private void LoadCatalog()
    {
        _definitions.Clear();
        var root = Path.Combine(AppContext.BaseDirectory, "catalog");
        if (!Directory.Exists(root))
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "catalog");
                if (Directory.Exists(candidate)) { root = candidate; break; }
            }
        }
        try
        {
            var catalog = new ReportCatalogLoader().Load(root);
            _definitions.AddRange(catalog.Reports);
            foreach (var error in catalog.Errors) Log.Warning("Invalid catalog file {CatalogFile}: {CatalogError}", error.File, error.Message);
            _reports.ItemsSource = _definitions;
            _reports.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ReportDefinition.Title));
            if (_definitions.Count > 0) _reports.SelectedIndex = 0;
            var versionFile = Path.Combine(root, "catalog.version");
            var version = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "не указана";
            _status.Text = $"Каталог v{version} • {_definitions.Count} отчёт(а)";
        }
        catch (Exception ex) { Log.Error(ex, "Catalog load failed"); _status.Text = "Не удалось загрузить каталог: " + ex.Message; }
    }

    private async Task RunReportAsync()
    {
        if (_reports.SelectedItem is not ReportDefinition report) return;
        // Avalonia controls are UI-thread-bound: snapshot parameter values before moving DB work off-thread.
        var from = _from.SelectedDate?.Date ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var to = _to.SelectedDate?.Date ?? DateTime.Today;
        _runCts = new CancellationTokenSource();
        _run.IsEnabled = _reports.IsEnabled = _reload.IsEnabled = false; _cancel.IsEnabled = true; _export.IsEnabled = false;
        _progress.IsVisible = true;
        _status.Text = "Выполняется запрос…";
        try
        {
            var result = await Task.Run(() => Fetch(report, from, to, _runCts.Token), _runCts.Token);
            _stagedCsvStage?.Dispose();
            _stagedCsvStage = result.Stage;
            _rowCount = result.RowCount;
            var data = result.Preview;
            _lastResult = data;
            _columnConfig = report.Result?.Columns ?? new(StringComparer.OrdinalIgnoreCase);
            _grid.ItemsSource = data.DefaultView;
            _export.IsEnabled = true;
            _status.Text = $"Показано {data.Rows.Count} из {_rowCount} строк" + (_rowCount > data.Rows.Count ? " (превью ограничено)" : "");
        }
        catch (OperationCanceledException) { _status.Text = "Запуск отменён"; }
        catch (Exception ex) { Log.Error(ex, "Report {ReportCode} failed", report.Code); _status.Text = "Ошибка отчёта: " + ex.Message; }
        finally { _progress.IsVisible = false; _run.IsEnabled = _reports.IsEnabled = _reload.IsEnabled = true; _cancel.IsEnabled = false; _runCts?.Dispose(); _runCts = null; }
    }

    private FetchResult Fetch(ReportDefinition report, DateTime from, DateTime to, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(report.Routine.Name, @"^[a-zA-Z_][a-zA-Z0-9_]*\.[a-zA-Z_][a-zA-Z0-9_]*$"))
            throw new InvalidOperationException("Недопустимое имя функции в каталоге");
        using var conn = new NpgsqlConnection(_connectionString); conn.Open();
        using (var check = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname=@schema AND p.proname=@name AND p.prokind='f')", conn))
        {
            var parts = report.Routine.Name.Split('.'); check.Parameters.AddWithValue("schema", parts[0]); check.Parameters.AddWithValue("name", parts[1]);
            if (!(bool)check.ExecuteScalar()!) throw new InvalidOperationException($"Функция {report.Routine.Name} отсутствует в PostgreSQL");
        }
        var parameters = report.Parameters;
        if (parameters.Count != 2 || parameters[0].Name != "p_from" || parameters[1].Name != "p_to")
            throw new NotSupportedException("В демо-клиенте настроены параметры типа date: p_from и p_to");
        using var cmd = new NpgsqlCommand($"SELECT * FROM {report.Routine.Name}(@p_from, @p_to)", conn) { CommandTimeout = report.Routine.TimeoutSec };
        cmd.Parameters.Add("p_from", NpgsqlTypes.NpgsqlDbType.Date).Value = DateOnly.FromDateTime(from);
        cmd.Parameters.Add("p_to", NpgsqlTypes.NpgsqlDbType.Date).Value = DateOnly.FromDateTime(to);
        var table = new DataTable(); using var reader = cmd.ExecuteReader();
        for (var c = 0; c < reader.FieldCount; c++) table.Columns.Add(reader.GetName(c), Nullable.GetUnderlyingType(reader.GetFieldType(c)) ?? reader.GetFieldType(c));
        var staged = new TemporaryCsvStage();
        long count = 0;
        try
        {
            using var writer = staged.OpenWriter();
            ReportExportWriter.WriteCsvRecord(writer, Enumerable.Range(0, reader.FieldCount).Select(reader.GetName));
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                var values = new object[reader.FieldCount]; reader.GetValues(values);
                ReportExportWriter.WriteCsvRecord(writer, Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? ReportExportWriter.NullMarker : Convert.ToString(values[i], CultureInfo.InvariantCulture) ?? ""));
                count++;
                if (table.Rows.Count < PreviewLimit) table.Rows.Add(values);
            }
            return new FetchResult(table, staged, count);
        }
        catch { staged.Dispose(); throw; }
    }

    private async Task ExportAsync()
    {
        if (_lastResult is null || _reports.SelectedItem is not ReportDefinition report) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Экспорт отчёта", SuggestedFileName = "sales-report.xlsx", FileTypeChoices = [new FilePickerFileType("Excel") { Patterns = ["*.xlsx"] }, new FilePickerFileType("CSV") { Patterns = ["*.csv"] }] });
        if (file is null) return;
        try
        {
            var path = file.Path.LocalPath;
            if (_stagedCsvStage is null || !File.Exists(_stagedCsvStage.Path)) throw new InvalidOperationException("Временная выгрузка отчёта недоступна");
            var saved = new ReportExportWriter().Save(_stagedCsvStage.Path, _lastResult, path, report.Result?.Sheet,
                report.Result?.Columns, _rowCount);
            _status.Text = saved.ExcelLimitExceeded
                ? $"Превышен лимит Excel — сохранён полный CSV: {Path.GetFileName(saved.Path)}"
                : "Файл сохранён: " + Path.GetFileName(saved.Path);
        }
        catch (Exception ex) { Log.Error(ex, "Export failed"); _status.Text = "Ошибка экспорта: " + ex.Message; }
    }

    private sealed record FetchResult(DataTable Preview, TemporaryCsvStage Stage, long RowCount);
}

