using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ErwinStudioSample;

public partial class MainWindow : Window
{
    private int _zoom = 100;
    private Border? _selectedCard;
    private Border? _draggedCard;
    private Point _entityDragStart;
    private double _entityStartLeft;
    private double _entityStartTop;
    private Path? _selectedRelationship;
    private Path? _selectedHitPath;
    private string? _selectedRelationshipKey;
    private readonly Dictionary<string, Vector> _relationshipOffsets = new();
    private bool _draggingRelationship;
    private Point _relationshipDragStart;
    private double _relationshipHandleStartX;
    private double _relationshipHandleStartY;

    private readonly Dictionary<string, ObservableCollection<ColumnInfo>> _columns = new()
    {
        ["Customer"] = [new("customer_id", "INT", true), new("first_name", "VARCHAR", true), new("last_name", "VARCHAR", true), new("email", "VARCHAR", true)],
        ["Order"] = [new("order_id", "INT", true), new("customer_id", "INT", true), new("order_date", "DATETIME", true), new("status", "VARCHAR", true)],
        ["OrderItem"] = [new("order_item_id", "INT", true), new("order_id", "INT", true), new("product_id", "INT", true), new("quantity", "INT", true)],
        ["Product"] = [new("product_id", "INT", true), new("category_id", "INT", true), new("name", "VARCHAR", true), new("unit_price", "DECIMAL", true)]
    };

    public MainWindow()
    {
        InitializeComponent();
        SelectEntity("Order");
        SelectCard(OrderCard);
        Loaded += (_, _) => { ResizeDiagramSurfaceToViewport(); UpdateRelationshipLines(); };
    }

    private void SelectEntity(string key)
    {
        var display = key == "OrderItem" ? "Order Item" : key;
        SelectedEntityTitle.Text = display.ToUpperInvariant();
        EntityNameBox.Text = display;
        PhysicalNameBox.Text = key switch { "OrderItem" => "order_item", "Order" => "sales_order", _ => key.ToLowerInvariant() };
        ColumnsGrid.ItemsSource = _columns.GetValueOrDefault(key, []);
        StatusText.Text = $"Selected entity: {display}";
    }

    private Border? FindCard(string key) => key switch
    {
        "Customer" => CustomerCard, "Order" => OrderCard, "Product" => ProductCard,
        "OrderItem" => OrderItemCard, _ => null
    };

    private void Entity_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Thumb || sender is not Border { Tag: string key } card) return;
        SelectEntity(key);
        SelectCard(card);
        ClearRelationshipSelection();
        _draggedCard = card;
        _entityDragStart = e.GetPosition(DiagramCanvasSurface);
        _entityStartLeft = Canvas.GetLeft(card);
        _entityStartTop = Canvas.GetTop(card);
        card.CaptureMouse();
        e.Handled = true;
    }

    private void Entity_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedCard is null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(DiagramCanvasSurface);
        Canvas.SetLeft(_draggedCard, Math.Max(8, _entityStartLeft + point.X - _entityDragStart.X));
        Canvas.SetTop(_draggedCard, Math.Max(8, _entityStartTop + point.Y - _entityDragStart.Y));
        UpdateRelationshipLines();
        StatusText.Text = $"Moving {_draggedCard.Tag}";
    }

    private void Entity_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedCard is null) return;
        _draggedCard.ReleaseMouseCapture();
        StatusText.Text = $"Moved entity: {_draggedCard.Tag}";
        _draggedCard = null;
        e.Handled = true;
    }

    private void SelectCard(Border card)
    {
        ClearCardSelection();
        _selectedCard = card;
        card.BorderBrush = new SolidColorBrush(Color.FromRgb(241, 191, 82));
        card.BorderThickness = new Thickness(2);
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(241, 191, 82), BlurRadius = 14, Opacity = .35, ShadowDepth = 0 };
        Panel.SetZIndex(card, 10);
    }

    private void ClearCardSelection()
    {
        if (_selectedCard is null) return;
        _selectedCard.ClearValue(Border.BorderBrushProperty);
        _selectedCard.ClearValue(Border.BorderThicknessProperty);
        _selectedCard.ClearValue(Border.EffectProperty);
        Panel.SetZIndex(_selectedCard, 1);
        _selectedCard = null;
    }

    private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is Thumb { Tag: string key } && FindCard(key) is { } card)
        {
            SelectEntity(key); SelectCard(card); ClearRelationshipSelection();
        }
        e.Handled = true;
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: string key } || FindCard(key) is not { } card) return;
        var scale = _zoom / 100d;
        card.Width = Math.Max(180, card.ActualWidth + e.HorizontalChange / scale);
        card.Height = Math.Max(130, card.ActualHeight + e.VerticalChange / scale);
        UpdateRelationshipLines();
        StatusText.Text = $"Resizing {key}: {card.Width:0} × {card.Height:0}";
        e.Handled = true;
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        StatusText.Text = "Entity resized"; e.Handled = true;
    }

    private Path? FindRelationshipLine(string key) => key switch
    {
        "CustomerOrder" => CustomerOrderLine, "OrderProduct" => OrderProductLine,
        "OrderOrderItem" => OrderOrderItemLine, "ProductOrderItem" => ProductOrderItemLine, _ => null
    };

    private void Relationship_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Path { Tag: string key } hitPath) return;
        ClearCardSelection(); ClearRelationshipSelection();
        _selectedHitPath = hitPath;
        _selectedRelationship = FindRelationshipLine(key);
        if (_selectedRelationship is null) return;
        _selectedRelationship.Stroke = new SolidColorBrush(Color.FromRgb(241, 223, 119));
        _selectedRelationship.StrokeThickness = 4;
        var point = e.GetPosition(DiagramCanvasSurface);
        RelationshipMoveHandle.Visibility = Visibility.Visible;
        Canvas.SetLeft(RelationshipMoveHandle, point.X - 9);
        Canvas.SetTop(RelationshipMoveHandle, point.Y - 9);
        StatusText.Text = "Relationship selected — drag the gold handle to move its route";
        e.Handled = true;
    }

    private void RelationshipHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectedRelationship is null) return;
        _draggingRelationship = true;
        _relationshipDragStart = e.GetPosition(DiagramCanvasSurface);
        _relationshipHandleStartX = Canvas.GetLeft(RelationshipMoveHandle);
        _relationshipHandleStartY = Canvas.GetTop(RelationshipMoveHandle);
        RelationshipMoveHandle.CaptureMouse(); e.Handled = true;
    }

    private void RelationshipHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingRelationship || _selectedRelationshipKey is null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(DiagramCanvasSurface);
        var delta = point - _relationshipDragStart;
        _relationshipOffsets.TryGetValue(_selectedRelationshipKey, out var original);
        _relationshipOffsets[_selectedRelationshipKey] = new Vector(original.X + delta.X, original.Y + delta.Y);
        _relationshipDragStart = point;
        UpdateRelationshipLines();
        StatusText.Text = "Moving relationship route — endpoints remain attached";
    }

    private void RelationshipHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingRelationship) return;
        _draggingRelationship = false;
        RelationshipMoveHandle.ReleaseMouseCapture();
        StatusText.Text = "Relationship route moved";
        e.Handled = true;
    }
    private void ClearRelationshipSelection()
    {
        if (_selectedRelationship is not null)
        {
            _selectedRelationship.Stroke = new SolidColorBrush(Color.FromRgb(66, 217, 212));
            _selectedRelationship.StrokeThickness = 2.5;
        }
        _selectedRelationship = null; _selectedHitPath = null; _selectedRelationshipKey = null;
        RelationshipMoveHandle.Visibility = Visibility.Collapsed;
    }

    private void DiagramCanvasSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source != DiagramCanvasSurface) return;
        ClearCardSelection(); ClearRelationshipSelection(); StatusText.Text = "Ready";
    }

    private void UpdateRelationshipLines()
    {
        UpdateRelationship("CustomerOrder", CustomerCard, OrderCard);
        UpdateRelationship("OrderProduct", OrderCard, ProductCard);
        UpdateRelationship("OrderOrderItem", OrderCard, OrderItemCard);
        UpdateRelationship("ProductOrderItem", ProductCard, OrderItemCard);
    }

    private void UpdateRelationship(string key, Border source, Border target)
    {
        var visible = FindRelationshipLine(key);
        var hit = DiagramCanvasSurface.Children.OfType<Path>()
            .FirstOrDefault(path => Equals(path.Tag, key) && path != visible);
        if (visible is null || hit is null) return;

        var sourceCenter = new Point(Canvas.GetLeft(source) + source.ActualWidth / 2, Canvas.GetTop(source) + source.ActualHeight / 2);
        var targetCenter = new Point(Canvas.GetLeft(target) + target.ActualWidth / 2, Canvas.GetTop(target) + target.ActualHeight / 2);
        var dx = targetCenter.X - sourceCenter.X;
        var dy = targetCenter.Y - sourceCenter.Y;
        var horizontal = Math.Abs(dx) >= Math.Abs(dy);
        Point start;
        Point end;
        List<Point> route;
        _relationshipOffsets.TryGetValue(key, out var offset);

        if (horizontal)
        {
            var direction = Math.Sign(dx == 0 ? 1 : dx);
            start = new Point(sourceCenter.X + direction * source.ActualWidth / 2, sourceCenter.Y);
            end = new Point(targetCenter.X - direction * target.ActualWidth / 2, targetCenter.Y);
            var startStub = new Point(start.X + direction * 24, start.Y);
            var endStub = new Point(end.X - direction * 24, end.Y);
            var trunkX = (startStub.X + endStub.X) / 2 + offset.X;
            var trunkY = (start.Y + end.Y) / 2 + offset.Y;
            route = [start, startStub, new Point(startStub.X, trunkY), new Point(trunkX, trunkY), new Point(endStub.X, trunkY), endStub, end];
        }
        else
        {
            var direction = Math.Sign(dy == 0 ? 1 : dy);
            start = new Point(sourceCenter.X, sourceCenter.Y + direction * source.ActualHeight / 2);
            end = new Point(targetCenter.X, targetCenter.Y - direction * target.ActualHeight / 2);
            var startStub = new Point(start.X, start.Y + direction * 24);
            var endStub = new Point(end.X, end.Y - direction * 24);
            var trunkX = (start.X + end.X) / 2 + offset.X;
            var trunkY = (startStub.Y + endStub.Y) / 2 + offset.Y;
            route = [start, startStub, new Point(trunkX, startStub.Y), new Point(trunkX, trunkY), new Point(trunkX, endStub.Y), endStub, end];
        }

        var figure = new PathFigure { StartPoint = route[0], IsClosed = false, IsFilled = false };
        foreach (var point in route.Skip(1)) figure.Segments.Add(new LineSegment(point, true));
        var geometry = new PathGeometry([figure]);
        visible.Data = geometry;
        hit.Data = geometry.Clone();

        if (_selectedRelationshipKey == key)
        {
            var middle = route[route.Count / 2];
            RelationshipMoveHandle.Visibility = Visibility.Visible;
            Canvas.SetLeft(RelationshipMoveHandle, middle.X - 9);
            Canvas.SetTop(RelationshipMoveHandle, middle.Y - 9);
        }
    }
    private void DiagramScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResizeDiagramSurfaceToViewport();
    }

    private void ResizeDiagramSurfaceToViewport()
    {
        if (DiagramScrollViewer is null || DiagramCanvasSurface is null) return;
        var scale = _zoom / 100d;
        DiagramCanvasSurface.Width = Math.Max(930, DiagramScrollViewer.ViewportWidth / scale);
        DiagramCanvasSurface.Height = Math.Max(650, DiagramScrollViewer.ViewportHeight / scale);
    }
    private void ModelTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: string key })
        {
            SelectEntity(key); if (FindCard(key) is { } card) SelectCard(card); ClearRelationshipSelection();
        }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(Math.Min(160, _zoom + 10));
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(Math.Max(50, _zoom - 10));
    private void SetZoom(int value) { _zoom = value; DiagramView.LayoutTransform = new ScaleTransform(_zoom / 100d, _zoom / 100d); ZoomLabel.Text = $"{_zoom}%"; ResizeDiagramSurfaceToViewport(); }
    private void Validate_Click(object sender, RoutedEventArgs e) { StatusText.Text = "Validation complete — no errors or warnings"; MessageBox.Show("Model validation completed successfully.\n\n5 entities checked\n4 relationships checked\n0 issues found", "Validate Model", MessageBoxButton.OK, MessageBoxImage.Information); }
    private void Generate_Click(object sender, RoutedEventArgs e) { const string ddl = "CREATE TABLE sales_order (\n  order_id INT NOT NULL PRIMARY KEY,\n  customer_id INT NOT NULL,\n  order_date DATETIME2 NOT NULL,\n  status VARCHAR(20) NOT NULL\n);"; Clipboard.SetText(ddl); StatusText.Text = "DDL generated and copied to clipboard"; MessageBox.Show(ddl + "\n\nCopied to clipboard.", "Generated SQL Server DDL", MessageBoxButton.OK, MessageBoxImage.Information); }
    private void NewEntity_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Entity creation workflow is ready for your implementation.", "New Entity", MessageBoxButton.OK, MessageBoxImage.Information);
    private void AddRelationship_Click(object sender, RoutedEventArgs e) => StatusText.Text = "Relationship tool active — choose parent and child entities";
    private void AddColumn_Click(object sender, RoutedEventArgs e) { if (ColumnsGrid.ItemsSource is ObservableCollection<ColumnInfo> items) items.Add(new("new_column", "VARCHAR", false)); }
    private void EntityNameBox_LostFocus(object sender, RoutedEventArgs e) => SelectedEntityTitle.Text = EntityNameBox.Text.ToUpperInvariant();
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (EmptyHint is null) return; EmptyHint.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text) || new[] { "customer", "order", "item", "product", "category" }.Any(x => x.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase)) ? Visibility.Collapsed : Visibility.Visible; StatusText.Text = string.IsNullOrWhiteSpace(SearchBox.Text) ? "Ready" : $"Filtering objects by ‘{SearchBox.Text}’"; }
}

public sealed record ColumnInfo(string Name, string Type, bool Required);