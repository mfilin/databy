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

/// <summary>Grid строки таблицы; только у таких сеток меняются ширины колонок.</summary>
public sealed class TableRowGrid : Grid;

public sealed class TableView : ListBox
{
    public static readonly StyledProperty<IEnumerable<TableColumnViewModel>?> ColumnsSourceProperty =
        AvaloniaProperty.Register<TableView, IEnumerable<TableColumnViewModel>?>(nameof(ColumnsSource));

    public static readonly StyledProperty<IEnumerable<TableRowViewModel>?> RowsSourceProperty =
        AvaloniaProperty.Register<TableView, IEnumerable<TableRowViewModel>?>(nameof(RowsSource));

    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#EDF1F7"));
    private static readonly IBrush HeaderBrush = new SolidColorBrush(Color.Parse("#F5F8FC"));
    private static readonly IBrush ResizeBrush = new SolidColorBrush(Color.Parse("#C9D4E4"));

    private readonly List<TableRowGrid> _rowGrids = [];
    private readonly HashSet<TableColumnViewModel> _subscribedColumns = [];
    private INotifyCollectionChanged? _subscribedCollection;
    private TableColumnViewModel? _resizingColumn;
    private Border? _activeHandle;
    private double _resizeStartX;
    private double _resizeStartWidth;

    protected override Type StyleKeyOverride => typeof(ListBox);

    public TableView()
    {
        ItemTemplate = new FuncDataTemplate<TableRowViewModel>((row, _) => BuildRow(row), supportsRecycling: false);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        SelectionMode = SelectionMode.Multiple;
        ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel());
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Auto);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Avalonia.Controls.Presenters.ItemsPresenter>("PART_ItemsPresenter") is { } presenter)
            presenter.HorizontalAlignment = HorizontalAlignment.Left;
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is not ListBoxItem listBoxItem) return;
        listBoxItem.HorizontalAlignment = HorizontalAlignment.Left;
        listBoxItem.HorizontalContentAlignment = HorizontalAlignment.Left;
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
            ResubscribeColumns();
            if (RowsSource is not null) ItemsSource = RowsSource;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResubscribeColumns();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeColumns();
        base.OnDetachedFromVisualTree(e);
    }

    private void ResubscribeColumns()
    {
        UnsubscribeColumns();
        if (ColumnsSource is null) return;

        foreach (var column in ColumnsSource)
            SubscribeColumn(column);

        if (ColumnsSource is INotifyCollectionChanged collection)
        {
            _subscribedCollection = collection;
            collection.CollectionChanged += OnColumnsCollectionChanged;
        }
    }

    private void UnsubscribeColumns()
    {
        foreach (var column in _subscribedColumns)
        {
            column.PropertyChanged -= OnColumnPropertyChanged;
        }
        _subscribedColumns.Clear();

        if (_subscribedCollection is not null)
        {
            _subscribedCollection.CollectionChanged -= OnColumnsCollectionChanged;
            _subscribedCollection = null;
        }
    }

    private void SubscribeColumn(TableColumnViewModel column)
    {
        if (!_subscribedColumns.Add(column)) return;
        column.PropertyChanged += OnColumnPropertyChanged;
    }

    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (TableColumnViewModel column in e.OldItems)
            {
                column.PropertyChanged -= OnColumnPropertyChanged;
                _subscribedColumns.Remove(column);
            }
        }
        if (e.NewItems is not null)
        {
            foreach (TableColumnViewModel column in e.NewItems)
                SubscribeColumn(column);
        }
        if (e.Action == NotifyCollectionChangedAction.Reset && ColumnsSource is not null)
        {
            _subscribedColumns.Clear();
            foreach (var column in ColumnsSource)
                SubscribeColumn(column);
        }
    }

    private void OnColumnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableColumnViewModel.Width)) ApplyColumnWidths();
    }


    private Control BuildRow(TableRowViewModel? row)
    {
        var grid = new TableRowGrid
        {
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions = BuildColumnDefinitions(),
            MinWidth = ColumnsSource?.Sum(column => column.Width) ?? 0,
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

            if (row.IsHeader)
                grid.Children.Add(BuildResizeHandle(index, row.Cells.Length));
        }

        _rowGrids.Add(grid);
        grid.DetachedFromVisualTree += (_, _) => _rowGrids.Remove(grid);
        return grid;
    }

    private Control BuildResizeHandle(int columnIndex, int columnCount)
    {
        var isLast = columnIndex == columnCount - 1;
        var handle = new Border
        {
            Width = 7,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
            Tag = columnIndex,
            ZIndex = 1,
            Margin = isLast ? new Thickness(0, 0, 7, 0) : default
        };
        handle.PointerPressed += OnResizePressed;
        handle.PointerMoved += OnResizeMoved;
        handle.PointerReleased += OnResizeReleased;
        handle.PointerEntered += (_, _) => handle.Background = ResizeBrush;
        handle.PointerExited += (_, _) => { if (_resizingColumn is null) handle.Background = Brushes.Transparent; };

        Grid.SetColumn(handle, columnIndex);
        return handle;
    }

    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: int index } border || ColumnsSource?.ToArray() is not { } columns || index >= columns.Length) return;
        e.Pointer.Capture(border);
        _activeHandle = border;
        _resizingColumn = columns[index];
        _resizeStartX = e.GetPosition(this).X;
        _resizeStartWidth = _resizingColumn.Width;
        e.Handled = true;
    }

    private void OnResizeMoved(object? sender, PointerEventArgs e)
    {
        if (_resizingColumn is null) return;
        var delta = e.GetPosition(this).X - _resizeStartX;
        _resizingColumn.Resize(_resizeStartWidth + delta);
        e.Handled = true;
    }

    private void OnResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        var handle = sender as Border ?? _activeHandle;
        if (handle is not null) handle.Background = Brushes.Transparent;
        if (_resizingColumn is null) return;
        _resizingColumn = null;
        _activeHandle = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ApplyColumnWidths()
    {
        var columns = ColumnsSource?.ToArray();
        if (columns is null || columns.Length == 0) return;
        var totalWidth = columns.Sum(column => column.Width);
        for (var index = _rowGrids.Count - 1; index >= 0; index--)
        {
            var grid = _rowGrids[index];
            if (grid.ColumnDefinitions.Count != columns.Length) continue;
            for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
                grid.ColumnDefinitions[columnIndex].Width = new GridLength(columns[columnIndex].Width, GridUnitType.Pixel);
            grid.MinWidth = totalWidth;
            grid.InvalidateMeasure();
            if (grid.Parent is Layoutable container)
            {
                container.MinWidth = totalWidth;
                container.InvalidateMeasure();
                (container.Parent as Layoutable)?.InvalidateMeasure();
            }
        }
        InvalidateMeasure();
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
