using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ErwinStudioSample;

public partial class MainWindow : Window
{
    private int _zoom = 100;
    private Border? _selectedCard;
    private Border? _selectedAttributeRow;
    private Border? _draggedCard;
    private Point _entityDragStart;
    private readonly DispatcherTimer _entityHoldTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private Border? _entityHoldCard;
    private Point _entityHoldStart;
    private readonly HashSet<string> _entityCardKeys = ["Customer", "Order", "Product", "OrderItem"];
    private double _entityStartLeft;
    private double _entityStartTop;
    private Path? _selectedRelationship;
    private Path? _selectedHitPath;
    private string? _selectedRelationshipKey;
    private readonly Dictionary<string, Vector> _relationshipOffsets = new();
    private readonly HashSet<string> _deletedRelationships = [];
    private readonly Dictionary<string, Border> _dynamicCards = new();
    private int _newEntityNumber;
    private int _newAnnotationNumber;
    private int _newViewNumber;
    private int _newMaterializedViewNumber;
    private int _newRelationshipNumber;
    private readonly Dictionary<string, DynamicRelationship> _dynamicRelationships = new();
    private string? _pendingRelationshipType;
    private string? _relationshipSourceKey;
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
        _entityHoldTimer.Tick += EntityHoldTimer_Tick;
        SelectEntity("Order");
        SelectCard(OrderCard);
        Loaded += (_, _) => { EnsureCloseButton(CustomerCard, "Customer"); EnsureCloseButton(OrderCard, "Order"); EnsureCloseButton(ProductCard, "Product"); EnsureCloseButton(OrderItemCard, "OrderItem"); PrepareAttributeRows(CustomerCard, "Customer"); PrepareAttributeRows(OrderCard, "Order"); PrepareAttributeRows(ProductCard, "Product"); PrepareAttributeRows(OrderItemCard, "OrderItem"); ResizeDiagramSurfaceToViewport(); UpdateRelationshipLines(); };
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

    private void EntityHoldTimer_Tick(object? sender, EventArgs e)
    {
        _entityHoldTimer.Stop();
        var card = _entityHoldCard;
        _entityHoldCard = null;
        if (card?.Tag is not string entityKey || !_entityCardKeys.Contains(entityKey)) return;
        _draggedCard = null;
        card.ReleaseMouseCapture();
        AddInlineColumnEditor(card, entityKey);
    }

    private void AddInlineColumnEditor(Border card, string entityKey)
    {
        if (card.Child is not Grid grid) return;
        var fields = grid.Children.OfType<StackPanel>().FirstOrDefault(panel => Grid.GetRow(panel) == 1);
        if (fields is null || fields.Children.OfType<Border>().Any(row => Equals(row.Tag, "inline-column-editor"))) return;

        var row = new Border
        {
            Tag = "inline-column-editor",
            Background = new SolidColorBrush(Color.FromRgb(38, 103, 113)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(72, 173, 180)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 3, 0, 2)
        };
        ClearAttributeSelection();
        _selectedAttributeRow = row;
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = new TextBlock { Text = "COL", Foreground = new SolidColorBrush(Color.FromRgb(207, 222, 235)), FontWeight = FontWeights.Bold, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        var editor = new TextBox { Text = "New attribute", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brushes.White, CaretBrush = Brushes.White, Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center };
        var datatype = new TextBlock { Text = "varchar(50)", Foreground = new SolidColorBrush(Color.FromRgb(166, 190, 211)), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(badge, 0); Grid.SetColumn(editor, 1); Grid.SetColumn(datatype, 2);
        layout.Children.Add(badge); layout.Children.Add(editor); layout.Children.Add(datatype);
        row.Child = layout;
        fields.Children.Add(row);
        card.Height = Math.Max(card.ActualHeight + 40, 150);
        _columns[entityKey].Add(new ColumnInfo("New attribute", "VARCHAR(50)", false));
        var columnIndex = _columns[entityKey].Count - 1;

        editor.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (editor.IsReadOnly) AttributeRow_MouseLeftButtonDown(row, args);
            else args.Handled = true;
        };
        editor.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            var name = string.IsNullOrWhiteSpace(editor.Text) ? "New attribute" : editor.Text.Trim();
            _columns[entityKey][columnIndex] = new ColumnInfo(name, "VARCHAR(50)", false);
            row.Tag = new AttributeSelection(entityKey, name);
            row.MouseLeftButtonDown += AttributeRow_MouseLeftButtonDown;
            editor.IsReadOnly = true;
            editor.Text = name;
            Keyboard.ClearFocus();
            StatusText.Text = $"Added column: {entityKey}.{name} varchar(50)";
            args.Handled = true;
        };
        SelectEntity(entityKey);
        SelectCard(card);
        editor.Focus();
        editor.SelectAll();
        StatusText.Text = "Type the column name and press Enter";
    }
    private void EnsureCloseButton(Border card, string key)
    {
        if (card.Child is not Grid cardGrid) return;
        var header = cardGrid.Children.OfType<Border>().FirstOrDefault(item => Grid.GetRow(item) == 0);
        if (header?.Child is not TextBlock title) return;
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Child = null;
        Grid.SetColumn(title, 0);
        headerGrid.Children.Add(title);
        var close = new Button
        {
            Tag = key,
            Content = "×",
            Width = 27,
            Height = 27,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 3, 5, 3),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 202, 222)),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            ToolTip = "Remove from diagram"
        };
        close.Click += DeleteObject_Click;
        Grid.SetColumn(close, 1);
        headerGrid.Children.Add(close);
        header.Child = headerGrid;
    }

    private void DeleteObject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        DeleteObject(key);
        e.Handled = true;
    }

    private void DeleteObject(string key)
    {
        var card = FindCard(key);
        if (card is null) return;
        if (_selectedCard == card) _selectedCard = null;
        if (_selectedAttributeRow is not null && _selectedAttributeRow.IsDescendantOf(card)) _selectedAttributeRow = null;

        var attached = _dynamicRelationships
            .Where(pair => pair.Value.SourceKey == key || pair.Value.TargetKey == key)
            .Select(pair => pair.Key)
            .ToList();
        foreach (var relationshipKey in attached)
        {
            var relationship = _dynamicRelationships[relationshipKey];
            DiagramCanvasSurface.Children.Remove(relationship.Visible);
            DiagramCanvasSurface.Children.Remove(relationship.Hit);
            _dynamicRelationships.Remove(relationshipKey);
            _relationshipOffsets.Remove(relationshipKey);
        }

        DiagramCanvasSurface.Children.Remove(card);
        _dynamicCards.Remove(key);
        _entityCardKeys.Remove(key);
        _columns.Remove(key);
        ClearRelationshipSelection();
        UpdateRelationshipLines();
        SelectedEntityTitle.Text = "NO SELECTION";
        ColumnsGrid.ItemsSource = null;
        StatusText.Text = $"Removed object: {key}";
    }
    private void PrepareAttributeRows(Border card, string entityKey)
    {
        if (card.Child is not Grid grid) return;
        var fields = grid.Children.OfType<StackPanel>().FirstOrDefault(panel => Grid.GetRow(panel) == 1);
        if (fields is null || !_columns.TryGetValue(entityKey, out var attributes)) return;
        var textRows = fields.Children.OfType<TextBlock>().ToList();
        for (var index = 0; index < textRows.Count && index < attributes.Count; index++)
        {
            var text = textRows[index];
            var childIndex = fields.Children.IndexOf(text);
            fields.Children.RemoveAt(childIndex);
            var row = new Border
            {
                Tag = new AttributeSelection(entityKey, attributes[index].Name),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                Child = text
            };
            row.MouseLeftButtonDown += AttributeRow_MouseLeftButtonDown;
            fields.Children.Insert(childIndex, row);
        }
    }

    private void AttributeRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: AttributeSelection selection } row) return;
        ClearAttributeSelection();
        _selectedAttributeRow = row;
        row.Background = new SolidColorBrush(Color.FromRgb(38, 103, 113));
        row.BorderBrush = new SolidColorBrush(Color.FromRgb(72, 173, 180));
        if (FindCard(selection.EntityKey) is { } card) SelectCard(card);
        SelectEntity(selection.EntityKey);
        StatusText.Text = $"Selected attribute: {selection.EntityKey}.{selection.AttributeName}";
        e.Handled = true;
    }

    private void ClearAttributeSelection()
    {
        if (_selectedAttributeRow is null) return;
        _selectedAttributeRow.Background = Brushes.Transparent;
        _selectedAttributeRow.BorderBrush = Brushes.Transparent;
        _selectedAttributeRow = null;
    }
    private Border? FindCard(string key)
    {
        if (_dynamicCards.TryGetValue(key, out var dynamicCard)) return dynamicCard;
        return key switch
        {
            "Customer" => CustomerCard, "Order" => OrderCard, "Product" => ProductCard,
            "OrderItem" => OrderItemCard, _ => null
        };
    }

    private void Entity_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Thumb || sender is not Border { Tag: string key } card) return;
        ClearAttributeSelection();
        if (_pendingRelationshipType is not null && _relationshipSourceKey is not null)
        {
            if (_relationshipSourceKey == key)
            {
                StatusText.Text = "Choose a different target entity";
                e.Handled = true;
                return;
            }
            CreateDynamicRelationship(_relationshipSourceKey, key, _pendingRelationshipType);
            _pendingRelationshipType = null;
            _relationshipSourceKey = null;
            e.Handled = true;
            return;
        }
        SelectEntity(key);
        SelectCard(card);
        ClearRelationshipSelection();
        _draggedCard = card;
        _entityDragStart = e.GetPosition(DiagramCanvasSurface);
        _entityStartLeft = Canvas.GetLeft(card);
        _entityStartTop = Canvas.GetTop(card);
        card.CaptureMouse();
        if (_entityCardKeys.Contains(key))
        {
            _entityHoldCard = card;
            _entityHoldStart = _entityDragStart;
            _entityHoldTimer.Stop();
            _entityHoldTimer.Start();
        }
        e.Handled = true;
    }

    private void Entity_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedCard is null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(DiagramCanvasSurface);
        if ((point - _entityHoldStart).Length > 5)
        {
            _entityHoldTimer.Stop();
            _entityHoldCard = null;
        }
        Canvas.SetLeft(_draggedCard, Math.Max(8, _entityStartLeft + point.X - _entityDragStart.X));
        Canvas.SetTop(_draggedCard, Math.Max(8, _entityStartTop + point.Y - _entityDragStart.Y));
        UpdateRelationshipLines();
        StatusText.Text = $"Moving {_draggedCard.Tag}";
    }

    private void Entity_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedCard is null) return;
        _entityHoldTimer.Stop();
        _entityHoldCard = null;
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

    private Path? FindRelationshipLine(string key)
    {
        if (_dynamicRelationships.TryGetValue(key, out var dynamicRelationship)) return dynamicRelationship.Visible;
        return key switch
        {
            "CustomerOrder" => CustomerOrderLine, "OrderProduct" => OrderProductLine,
            "OrderOrderItem" => OrderOrderItemLine, "ProductOrderItem" => ProductOrderItemLine, _ => null
        };
    }

    private void Relationship_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Path { Tag: string key } hitPath) return;
        ClearCardSelection(); ClearRelationshipSelection();
        _selectedHitPath = hitPath;
                _selectedRelationshipKey = key;
_selectedRelationship = FindRelationshipLine(key);
        if (_selectedRelationship is null) return;
        _selectedRelationship.Stroke = new SolidColorBrush(Color.FromRgb(241, 223, 119));
        _selectedRelationship.StrokeThickness = 4;
        var point = e.GetPosition(DiagramCanvasSurface);
        RelationshipMoveHandle.Visibility = Visibility.Visible;
        Canvas.SetLeft(RelationshipMoveHandle, point.X - 9);
        Canvas.SetTop(RelationshipMoveHandle, point.Y - 9);
        RelationshipDeleteButton.Tag = key;
        RelationshipDeleteButton.Visibility = Visibility.Visible;
        Canvas.SetLeft(RelationshipDeleteButton, point.X - 13);
        Canvas.SetTop(RelationshipDeleteButton, point.Y - 47);
        StatusText.Text = "Relationship selected — drag the line or gold handle to bend its route";
        _draggingRelationship = true;
        _relationshipDragStart = point;
        hitPath.CaptureMouse();
        e.Handled = true;
    }

    private void RelationshipHit_MouseMove(object sender, MouseEventArgs e)
    {
        RelationshipHandle_MouseMove(sender, e);
    }

    private void RelationshipHit_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CompleteRelationshipDrag(e);
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
        CompleteRelationshipDrag(e);
    }

    private void CompleteRelationshipDrag(MouseButtonEventArgs e)
    {
        if (!_draggingRelationship) return;
        _draggingRelationship = false;
        if (Mouse.Captured is UIElement captured) captured.ReleaseMouseCapture();
        StatusText.Text = "Relationship route moved; endpoints remain connected";
        e.Handled = true;
    }
    private void RelationshipDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (RelationshipDeleteButton.Tag is not string key) return;
        var selectedVisible = _selectedRelationship;
        var selectedHit = _selectedHitPath;
        ClearRelationshipSelection();
        if (_dynamicRelationships.Remove(key, out var dynamicRelationship))
        {
            DiagramCanvasSurface.Children.Remove(dynamicRelationship.Visible);
            DiagramCanvasSurface.Children.Remove(dynamicRelationship.Hit);
        }
        else
        {
            _deletedRelationships.Add(key);
            if (selectedVisible is not null) selectedVisible.Visibility = Visibility.Collapsed;
            if (selectedHit is not null) selectedHit.Visibility = Visibility.Collapsed;
        }
        _relationshipOffsets.Remove(key);
        StatusText.Text = "Relationship removed";
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
        RelationshipDeleteButton.Visibility = Visibility.Collapsed;
        RelationshipDeleteButton.Tag = null;
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
        foreach (var (key, relationship) in _dynamicRelationships)
            if (FindCard(relationship.SourceKey) is { } source && FindCard(relationship.TargetKey) is { } target)
                UpdateRelationship(key, source, target);
    }

    private void UpdateRelationship(string key, Border source, Border target)
    {
        var visible = FindRelationshipLine(key);
        var hit = DiagramCanvasSurface.Children.OfType<Path>()
            .FirstOrDefault(path => Equals(path.Tag, key) && path != visible);
        if (visible is null || hit is null) return;
        if (_deletedRelationships.Contains(key) ||
            !DiagramCanvasSurface.Children.Contains(source) ||
            !DiagramCanvasSurface.Children.Contains(target))
        {
            visible.Visibility = Visibility.Collapsed;
            hit.Visibility = Visibility.Collapsed;
            return;
        }
        visible.Visibility = Visibility.Visible;
        hit.Visibility = Visibility.Visible;

        var sourceCenter = new Point(Canvas.GetLeft(source) + source.ActualWidth / 2, Canvas.GetTop(source) + source.ActualHeight / 2);
        var targetCenter = new Point(Canvas.GetLeft(target) + target.ActualWidth / 2, Canvas.GetTop(target) + target.ActualHeight / 2);
        var dx = targetCenter.X - sourceCenter.X;
        var dy = targetCenter.Y - sourceCenter.Y;
        var horizontal = Math.Abs(dx) >= Math.Abs(dy);
        _relationshipOffsets.TryGetValue(key, out var offset);
        List<Point> route;
        Point handlePoint;

        if (horizontal)
        {
            var direction = Math.Sign(dx == 0 ? 1 : dx);
            var start = new Point(sourceCenter.X + direction * source.ActualWidth / 2, sourceCenter.Y);
            var end = new Point(targetCenter.X - direction * target.ActualWidth / 2, targetCenter.Y);
            var startStub = new Point(start.X + direction * 24, start.Y);
            var endStub = new Point(end.X - direction * 24, end.Y);
            var trunkY = (start.Y + end.Y) / 2 + offset.Y;
            route = [start, startStub, new Point(startStub.X, trunkY), new Point(endStub.X, trunkY), endStub, end];
            handlePoint = new Point((startStub.X + endStub.X) / 2, trunkY);
        }
        else
        {
            var direction = Math.Sign(dy == 0 ? 1 : dy);
            var start = new Point(sourceCenter.X, sourceCenter.Y + direction * source.ActualHeight / 2);
            var end = new Point(targetCenter.X, targetCenter.Y - direction * target.ActualHeight / 2);
            var startStub = new Point(start.X, start.Y + direction * 24);
            var endStub = new Point(end.X, end.Y - direction * 24);
            var trunkX = (start.X + end.X) / 2 + offset.X;
            route = [start, startStub, new Point(trunkX, startStub.Y), new Point(trunkX, endStub.Y), endStub, end];
            handlePoint = new Point(trunkX, (startStub.Y + endStub.Y) / 2);
        }

        var figure = new PathFigure { StartPoint = route[0], IsClosed = false, IsFilled = false };
        foreach (var point in route.Skip(1)) figure.Segments.Add(new LineSegment(point, true));
        var geometry = new PathGeometry([figure]);
        visible.Data = geometry;
        hit.Data = geometry.Clone();

        if (_selectedRelationshipKey == key)
        {
            RelationshipMoveHandle.Visibility = Visibility.Visible;
            Canvas.SetLeft(RelationshipMoveHandle, handlePoint.X - 9);
            Canvas.SetTop(RelationshipMoveHandle, handlePoint.Y - 9);
            RelationshipDeleteButton.Tag = key;
            RelationshipDeleteButton.Visibility = Visibility.Visible;
            Canvas.SetLeft(RelationshipDeleteButton, handlePoint.X - 13);
            Canvas.SetTop(RelationshipDeleteButton, handlePoint.Y - 47);
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
    private void NewEntity_Click(object sender, RoutedEventArgs e) => AddNewEntity();
    private void DiagramTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ToolTip: string tool }) return;
        switch (tool)
        {
            case "Entity":
                AddNewEntity();
                return;
            case "Annotation":
                AddNewAnnotation();
                return;
            case "View":
                AddNewView(false);
                return;
            case "Materialized View":
                AddNewView(true);
                return;
            case "Identifying relationship":
            case "Non-identifying relationship":
                if (_selectedCard?.Tag is not string sourceKey)
                {
                    StatusText.Text = "Select the source entity first";
                    return;
                }
                _relationshipSourceKey = sourceKey;
                _pendingRelationshipType = tool;
                StatusText.Text = $"{tool}: now select the target entity";
                return;
            default:
                StatusText.Text = $"{tool} tool selected — click the diagram to place it";
                return;
        }
    }
    private void CreateDynamicRelationship(string sourceKey, string targetKey, string relationshipType)
    {
        var source = FindCard(sourceKey);
        var target = FindCard(targetKey);
        if (source is null || target is null) return;

        var key = $"DynamicRelationship{++_newRelationshipNumber}";
        var hit = new Path
        {
            Tag = key, Stroke = Brushes.Transparent, StrokeThickness = 16,
            Cursor = Cursors.SizeAll
        };
        hit.MouseLeftButtonDown += Relationship_MouseLeftButtonDown;
        hit.MouseMove += RelationshipHit_MouseMove;
        hit.MouseLeftButtonUp += RelationshipHit_MouseLeftButtonUp;

        var visible = new Path
        {
            Tag = key,
            Stroke = new SolidColorBrush(Color.FromRgb(66, 217, 212)),
            StrokeThickness = 2.5,
            IsHitTestVisible = false,
            StrokeLineJoin = PenLineJoin.Round
        };
        if (relationshipType == "Non-identifying relationship")
        {
            visible.StrokeDashArray = new DoubleCollection([2, 1.5]);
            visible.StrokeDashCap = PenLineCap.Round;
        }

        Panel.SetZIndex(hit, 0);
        Panel.SetZIndex(visible, 0);
        DiagramCanvasSurface.Children.Add(hit);
        DiagramCanvasSurface.Children.Add(visible);
        _dynamicRelationships[key] = new DynamicRelationship(sourceKey, targetKey, visible, hit, relationshipType == "Identifying relationship");
        UpdateRelationship(key, source, target);

        ClearCardSelection();
        ClearRelationshipSelection();
        _selectedHitPath = hit;
        _selectedRelationshipKey = key;
        _selectedRelationship = visible;
        visible.Stroke = new SolidColorBrush(Color.FromRgb(241, 223, 119));
        visible.StrokeThickness = 4;
        var bounds = visible.Data.Bounds;
        RelationshipMoveHandle.Visibility = Visibility.Visible;
        Canvas.SetLeft(RelationshipMoveHandle, bounds.Left + bounds.Width / 2 - 9);
        Canvas.SetTop(RelationshipMoveHandle, bounds.Top + bounds.Height / 2 - 9);
        StatusText.Text = $"Created {relationshipType}: {sourceKey} → {targetKey}";
    }
    private void AddNewAnnotation()
    {
        _newAnnotationNumber++;
        var key = _newAnnotationNumber == 1 ? "NewAnnotation" : $"NewAnnotation{_newAnnotationNumber}";
        _columns[key] = [];
        var card = CreateInteractiveCard(key, 160, 105);
        card.Background = new SolidColorBrush(Color.FromRgb(239, 244, 250));
        card.BorderBrush = new SolidColorBrush(Color.FromRgb(137, 157, 179));
        card.BorderThickness = new Thickness(1.5);
        card.CornerRadius = new CornerRadius(0);
        card.Cursor = Cursors.SizeAll;

        var grid = new Grid();
        var editor = new TextBox
        {
            Text = "Type annotation",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(31, 47, 67)),
            Margin = new Thickness(0, 22, 0, 0),
            Padding = new Thickness(12, 4, 12, 12),
            Cursor = Cursors.IBeam,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        editor.PreviewMouseLeftButtonDown += (_, e) => { SelectEntity(key); SelectCard(card); editor.Focus(); e.Handled = true; };
        editor.GotKeyboardFocus += (_, _) => { SelectEntity(key); SelectCard(card); };
        grid.Children.Add(editor);
        grid.Children.Add(CreateResizeThumb(key));
        card.Child = grid;
        AddDynamicCardToCanvas(key, card);
        StatusText.Text = $"Created annotation: {key}";
    }

    private void AddNewView(bool materialized)
    {
        var number = materialized ? ++_newMaterializedViewNumber : ++_newViewNumber;
        var baseName = materialized ? "NewMaterializedView" : "NewView";
        var key = number == 1 ? baseName : $"{baseName}{number}";
        _columns[key] = [new("Column1", "VARCHAR(50)", false)];
        var card = CreateInteractiveCard(key, materialized ? 270 : 245, 125);
        card.BorderBrush = new SolidColorBrush(materialized ? Color.FromRgb(241, 184, 63) : Color.FromRgb(68, 211, 218));
        card.BorderThickness = new Thickness(1.5);

        var grid = new Grid { Background = Brushes.Transparent };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(32, 43, 58)),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Child = new TextBlock { Text = key, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }
        };
        grid.Children.Add(header);
        var fields = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
        fields.Children.Add(new TextBlock { Text = "COL   Column1              VARCHAR(50)", Foreground = new SolidColorBrush(Color.FromRgb(220, 231, 245)), Margin = new Thickness(0, 3, 0, 3) });
        Grid.SetRow(fields, 1);
        grid.Children.Add(fields);
        grid.Children.Add(CreateResizeThumb(key));
        card.Child = grid;
        EnsureCloseButton(card, key);
        PrepareAttributeRows(card, key);
        AddDynamicCardToCanvas(key, card);
        StatusText.Text = $"Created {(materialized ? "materialized view" : "view")}: {key}";
    }

    private Border CreateInteractiveCard(string key, double width, double height)
    {
        var card = new Border { Tag = key, Width = width, Height = height, Style = (Style)FindResource("EntityCard") };
        card.MouseLeftButtonDown += Entity_MouseLeftButtonDown;
        card.MouseMove += Entity_MouseMove;
        card.MouseLeftButtonUp += Entity_MouseLeftButtonUp;
        return card;
    }

    private Thumb CreateResizeThumb(string key)
    {
        var resize = new Thumb { Tag = key, Style = (Style)FindResource("CardResizeThumb") };
        resize.DragStarted += ResizeThumb_DragStarted;
        resize.DragDelta += ResizeThumb_DragDelta;
        resize.DragCompleted += ResizeThumb_DragCompleted;
        Grid.SetRowSpan(resize, 2);
        return resize;
    }

    private void AddDynamicCardToCanvas(string key, Border card)
    {
        _dynamicCards[key] = card;
        Panel.SetZIndex(card, 1);
        DiagramCanvasSurface.Children.Add(card);
        var scale = _zoom / 100d;
        var sequence = _dynamicCards.Count;
        var offset = (sequence % 5) * 18;
        var left = (DiagramScrollViewer.HorizontalOffset + DiagramScrollViewer.ViewportWidth / 2) / scale - card.Width / 2 + offset;
        var top = (DiagramScrollViewer.VerticalOffset + DiagramScrollViewer.ViewportHeight / 2) / scale - card.Height / 2 + offset;
        Canvas.SetLeft(card, Math.Max(16, left));
        Canvas.SetTop(card, Math.Max(16, top));
        SelectEntity(key);
        SelectCard(card);
        ClearRelationshipSelection();
    }
    private void AddNewEntity()
    {
        _newEntityNumber++;
        var key = _newEntityNumber == 1 ? "NewEntity" : $"NewEntity{_newEntityNumber}";
        _columns[key] = [new("Id", "UUID", true)];
        _entityCardKeys.Add(key);

        var card = new Border
        {
            Tag = key,
            Width = 230,
            Height = 110,
            Style = (Style)FindResource("EntityCard")
        };
        card.MouseLeftButtonDown += Entity_MouseLeftButtonDown;
        card.MouseMove += Entity_MouseMove;
        card.MouseLeftButtonUp += Entity_MouseLeftButtonUp;

        var grid = new Grid { Background = Brushes.Transparent };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(13, 139, 151)),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Child = new TextBlock
            {
                Text = key,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                Margin = new Thickness(12, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var fields = new StackPanel { Margin = new Thickness(10, 7, 10, 7) };
        fields.Children.Add(new TextBlock
        {
            Text = "🔑   Id                         UUID",
            Foreground = new SolidColorBrush(Color.FromRgb(220, 231, 245)),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 3, 0, 3)
        });
        fields.Children.Add(new Separator());
        Grid.SetRow(fields, 1);
        grid.Children.Add(fields);

        var resize = new Thumb
        {
            Tag = key,
            Style = (Style)FindResource("CardResizeThumb")
        };
        resize.DragStarted += ResizeThumb_DragStarted;
        resize.DragDelta += ResizeThumb_DragDelta;
        resize.DragCompleted += ResizeThumb_DragCompleted;
        Grid.SetRowSpan(resize, 2);
        grid.Children.Add(resize);

        card.Child = grid;
        EnsureCloseButton(card, key);
        PrepareAttributeRows(card, key);
        _dynamicCards[key] = card;
        DiagramCanvasSurface.Children.Add(card);

        var scale = _zoom / 100d;
        var left = (DiagramScrollViewer.HorizontalOffset + DiagramScrollViewer.ViewportWidth / 2) / scale - card.Width / 2;
        var top = (DiagramScrollViewer.VerticalOffset + DiagramScrollViewer.ViewportHeight / 2) / scale - card.Height / 2;
        Canvas.SetLeft(card, Math.Max(16, left));
        Canvas.SetTop(card, Math.Max(16, top));
        SelectEntity(key);
        SelectCard(card);
        ClearRelationshipSelection();
        StatusText.Text = $"Created entity: {key}";
    }
    private void AddRelationship_Click(object sender, RoutedEventArgs e) => StatusText.Text = "Relationship tool active — choose parent and child entities";
    private void AddColumn_Click(object sender, RoutedEventArgs e) { if (ColumnsGrid.ItemsSource is ObservableCollection<ColumnInfo> items) items.Add(new("new_column", "VARCHAR", false)); }
    private void EntityNameBox_LostFocus(object sender, RoutedEventArgs e) => SelectedEntityTitle.Text = EntityNameBox.Text.ToUpperInvariant();
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (EmptyHint is null) return; EmptyHint.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text) || new[] { "customer", "order", "item", "product", "category" }.Any(x => x.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase)) ? Visibility.Collapsed : Visibility.Visible; StatusText.Text = string.IsNullOrWhiteSpace(SearchBox.Text) ? "Ready" : $"Filtering objects by ‘{SearchBox.Text}’"; }
}

public sealed record ColumnInfo(string Name, string Type, bool Required);

public sealed record DynamicRelationship(string SourceKey, string TargetKey, Path Visible, Path Hit, bool IsIdentifying);

public sealed record AttributeSelection(string EntityKey, string AttributeName);
