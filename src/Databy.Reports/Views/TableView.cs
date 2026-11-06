using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
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
    private static readonly IBrush ResizeBrush = new SolidColorBrush(Color.Parse("#C9D4E4"));

    private readonly List<Grid> _rowGrids = [];
    private TableColumnViewModel? _resizingColumn;
    private double _resizeStartX;
    private double _resizeStartWidth;

    static TableView()
    {
        ScrollViewer.HorizontalScrollBarVisibilityProperty.OverrideDefaultValue<TableView>(ScrollBarVisibility.Auto);
        ScrollViewer.VerticalScrollBarVisibilityProperty.OverrideDefaultValue<TableView>(ScrollBarVisibility.Auto);
    }

    protected override Type StyleKeyOverride => typeof(ListBox);

    public TableView()
    {
        ItemTemplate = new FuncDataTemplate<TableRowViewModel>((row, _) => BuildRow(row), supportsRecycling: false);
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
        {
            _rowGrids.Clear();
            ItemsSource = RowsSource;
        }
        else if (change.Property == ColumnsSourceProperty)
        {
            _rowGrids.Clear();
            if (RowsSource is not null) ItemsSource = RowsSource;
            SubscribeToColumns();
            ApplyColumnWidths();
        }
    }

    private void SubscribeToColumns()
    {
        if (ColumnsSource is null) return;
        foreach (var column in ColumnsSource)
            column.PropertyChanged -= OnColumnPropertyChanged;
        foreach (var column in ColumnsSource)
            column.PropertyChanged += OnColumnPropertyChanged;

        if (ColumnsSource is INotifyCollectionChanged notifying)
        {
            notifying.CollectionChanged -= (_, _) => SubscribeToColumns();
            notifying.CollectionChanged += (_, _) => SubscribeToColumns();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToColumns();
        ApplyColumnWidths();
    }

    private void OnColumnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TableColumnViewModel.Width)) return;
        ApplyColumnWidths();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        ApplyColumnWidths();
    }

    private Control BuildRow(TableRowViewModel? row)
    {
        var grid = new Grid
        {
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
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

            if (row.IsHeader && index < row.Cells.Length - 1)
                grid.Children.Add(BuildResizeHandle(index));
        }

        _rowGrids.Add(grid);
        grid.AttachedToVisualTree += (_, _) => ApplyColumnWidths();
        grid.DetachedFromVisualTree += (_, _) => _rowGrids.Remove(grid);
        return grid;
    }

    private Control BuildResizeHandle(int columnIndex)
    {
        var handle = new Border
        {
            Width = 7,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
            Tag = columnIndex,
            ZIndex = 1
        };
        handle.PointerPressed += OnResizePressed;
        handle.PointerMoved += OnResizeMoved;
        handle.PointerReleased += OnResizeReleased;
        handle.PointerEntered += (_, _) => handle.Background = ResizeBrush;
        handle.PointerExited += (_, _) => { if (_resizingColumn is null) handle.Background = Brushes.Transparent; };

        var overlay = new Panel { HorizontalAlignment = HorizontalAlignment.Right, Width = 7, Tag = columnIndex, ZIndex = 1 };
        overlay.Children.Add(handle);
        overlay.PointerPressed += OnResizePressed;
        overlay.PointerMoved += OnResizeMoved;
        overlay.PointerReleased += OnResizeReleased;
        overlay.PointerEntered += (_, _) => handle.Background = ResizeBrush;
        overlay.PointerExited += (_, _) => { if (_resizingColumn is null) handle.Background = Brushes.Transparent; };
        Grid.SetColumn(overlay, columnIndex);
        return overlay;
    }

    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: int index } || ColumnsSource?.ToArray() is not { } columns || index >= columns.Length) return;
        e.Pointer.Capture(sender as IInputElement);
        _resizingColumn = columns[index];
        _resizeStartX = e.GetPosition(this).X;
        _resizeStartWidth = _resizingColumn.Width;
        e.Handled = true;
    }

    private void OnResizeMoved(object? sender, PointerEventArgs e)
    {
        if (_resizingColumn is null || e.Pointer.Captured is not Control) return;
        var delta = e.GetPosition(this).X - _resizeStartX;
        _resizingColumn.Resize(_resizeStartWidth + delta);
        ApplyColumnWidths();
        e.Handled = true;
    }

    private void OnResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizingColumn is null) return;
        _resizingColumn = null;
        e.Pointer.Capture(null);
        if (sender is Border { } border) border.Background = Brushes.Transparent;
        e.Handled = true;
    }

    private void ApplyColumnWidths()
    {
        var columns = ColumnsSource?.ToArray();
        if (columns is null || columns.Length == 0) return;
        foreach (var grid in _rowGrids.Concat(this.GetVisualDescendants().OfType<Grid>()).Distinct().ToArray())
        {
            if (grid.ColumnDefinitions.Count != columns.Length) continue;
            for (var index = 0; index < columns.Length; index++)
                grid.ColumnDefinitions[index].Width = new GridLength(columns[index].Width, GridUnitType.Pixel);
            grid.InvalidateMeasure();
            grid.InvalidateArrange();
        }
    }

    private ColumnDefinitions BuildColumnDefinitions()
    {
        var definitions = new ColumnDefinitions();
        var columns = ColumnsSource?.ToArray();
        if (columns is null || columns.Length == 0)
        {
            definitions.Add(new ColumnDefinition(TableColumnViewModel.DefaultWidth, GridUnitType.Pixel));
            return definitions;
        }
        foreach (var column in columns)
            definitions.Add(new ColumnDefinition(column.Width, GridUnitType.Pixel));
        return definitions;
    }
}
