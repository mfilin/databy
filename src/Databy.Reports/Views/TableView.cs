using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Databy.Reports.ViewModels;

namespace Databy.Reports.Views;

public sealed class TableView : ListBox
{
    public static readonly StyledProperty<IEnumerable<TableColumnViewModel>?> ColumnsSourceProperty =
        AvaloniaProperty.Register<TableView, IEnumerable<TableColumnViewModel>?>(nameof(ColumnsSource));

    public static readonly StyledProperty<IEnumerable<TableRowViewModel>?> RowsSourceProperty =
        AvaloniaProperty.Register<TableView, IEnumerable<TableRowViewModel>?>(nameof(RowsSource));

    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#EDF1F7"));
    private static readonly IBrush HeaderBrush = new SolidColorBrush(Color.Parse("#F5F8FC"));

    static TableView()
    {
        ScrollViewer.HorizontalScrollBarVisibilityProperty.OverrideDefaultValue<TableView>(ScrollBarVisibility.Auto);
        ScrollViewer.VerticalScrollBarVisibilityProperty.OverrideDefaultValue<TableView>(ScrollBarVisibility.Auto);
    }

    protected override Type StyleKeyOverride => typeof(ListBox);

    public TableView()
    {
        ItemTemplate = new FuncDataTemplate<TableRowViewModel>((row, _) => BuildRow(row), supportsRecycling: true);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        SelectionMode = SelectionMode.Multiple;
    }

    public IEnumerable<TableColumnViewModel>? ColumnsSource
    {
        get => GetValue(ColumnsSourceProperty);
        set => SetValue(ColumnsSourceProperty, value);
    }

    public IEnumerable<TableRowViewModel>? RowsSource
    {
        get => GetValue(RowsSourceProperty);
        set => SetValue(RowsSourceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowsSourceProperty)
            ItemsSource = RowsSource;
    }

    private Control BuildRow(TableRowViewModel? row)
    {
        var grid = new Grid
        {
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions = BuildColumnDefinitions(),
            Background = row?.IsHeader == true ? HeaderBrush : Brushes.Transparent
        };
        if (row is null) return grid;

        for (var index = 0; index < row.Cells.Length; index++)
        {
            var cell = new Border
            {
                BorderBrush = GridBrush,
                BorderThickness = new Thickness(0, 0, index == row.Cells.Length - 1 ? 0 : 1, 0),
                Padding = new Thickness(12, 0),
                Child = new TextBlock
                {
                    Text = row.Cells[index],
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = row.IsHeader ? FontWeight.SemiBold : FontWeight.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            Grid.SetColumn(cell, index);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private ColumnDefinitions BuildColumnDefinitions()
    {
        var definitions = new ColumnDefinitions();
        var columns = ColumnsSource?.ToArray();
        if (columns is null || columns.Length == 0)
        {
            definitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            return definitions;
        }
        foreach (var column in columns)
            definitions.Add(column.Width is { } width
                ? new ColumnDefinition(width, GridUnitType.Pixel)
                : new ColumnDefinition(1, GridUnitType.Star));
        return definitions;
    }
}
