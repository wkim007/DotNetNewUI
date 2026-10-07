using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private void InitializeDrawingPalette()
    {
        DiagramCanvasSurface.PreviewMouseLeftButtonDown += DrawingConnectorTarget_Click;
        var shapes = new[] { ("Rectangle", "□"), ("Round Rectangle", "▢"), ("Ellipse", "○"), ("Diamond", "◇"),
            ("Hexagon", "⬡"), ("Octogon", "⬢"), ("Parallelogram", "▱"), ("Pentagon", "⬠"),
            ("Star", "★"), ("Cross", "✚"), ("Triangle Up", "▲"), ("Triangle Down", "▼"),
            ("Triangle Left", "◀"), ("Triangle Right", "▶"), ("Connector", "╱") };
        foreach (var (name, icon) in shapes)
        {
            var button = new Button { Content = name, Tag = icon, ToolTip = name, Height = 76, MinWidth = 0,
                Style = (Style)FindResource("DiagramToolButton"), Margin = new Thickness(3) };
            button.Click += (_, _) => { DrawingPalette.Visibility = Visibility.Collapsed; if (name == "Connector") BeginDrawingConnector(); else AddDrawingShape(name); };
            DrawingShapeButtons.Children.Add(button);
        }
        PreviewMouseDown += (_, args) =>
        {
            if (DrawingPalette.Visibility != Visibility.Visible) return;
            if (args.OriginalSource is DependencyObject source &&
                (DrawingPalette.IsAncestorOf(source) || DrawingToolButton.IsAncestorOf(source) || source == DrawingPalette || source == DrawingToolButton)) return;
            DrawingPalette.Visibility = Visibility.Collapsed;
        };
        Deactivated += (_, _) => DrawingPalette.Visibility = Visibility.Collapsed;
        PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) { DrawingPalette.Visibility = Visibility.Collapsed; _connectorSource = null; } };
    }

    private void DrawingTool_Click(object sender, RoutedEventArgs e)
    {
        DrawingPalette.Visibility = DrawingPalette.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (DrawingPalette.Visibility == Visibility.Visible)
            Dispatcher.BeginInvoke(new Action(() => DrawingPalette.BringIntoView()));
    }

    private void AddDrawingShape(string name)
    {
        const double width = 140, height = 100;
        Geometry Polygon(params Point[] points)
        {
            var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
            foreach (var point in points.Skip(1)) figure.Segments.Add(new LineSegment(point, true));
            return new PathGeometry([figure]);
        }
        Geometry Regular(int count, bool star = false)
        {
            var points = Enumerable.Range(0, count).Select(i =>
            {
                var angle = -Math.PI / 2 + i * Math.PI * 2 / count;
                var radius = star && i % 2 == 1 ? .43 : 1;
                return new Point(70 + Math.Cos(angle) * 66 * radius, 50 + Math.Sin(angle) * 46 * radius);
            }).ToArray();
            return Polygon(points);
        }
        Geometry geometry = name switch
        {
            "Rectangle" => new RectangleGeometry(new Rect(3, 3, 134, 94)),
            "Round Rectangle" => new RectangleGeometry(new Rect(3, 3, 134, 94), 16, 16),
            "Ellipse" => new EllipseGeometry(new Rect(3, 3, 134, 94)),
            "Diamond" => Polygon(new(70, 3), new(137, 50), new(70, 97), new(3, 50)),
            "Hexagon" => Regular(6), "Octogon" => Regular(8), "Pentagon" => Regular(5), "Star" => Regular(10, true),
            "Parallelogram" => Polygon(new(28, 3), new(137, 3), new(112, 97), new(3, 97)),
            "Cross" => Polygon(new(50, 3), new(90, 3), new(90, 30), new(137, 30), new(137, 70), new(90, 70), new(90, 97), new(50, 97), new(50, 70), new(3, 70), new(3, 30), new(50, 30)),
            "Triangle Up" => Polygon(new(70, 3), new(137, 97), new(3, 97)),
            "Triangle Down" => Polygon(new(3, 3), new(137, 3), new(70, 97)),
            "Triangle Left" => Polygon(new(3, 50), new(137, 3), new(137, 97)),
            "Triangle Right" => Polygon(new(3, 3), new(137, 50), new(3, 97)),
            _ => new LineGeometry(new Point(3, 97), new Point(137, 3))
        };
        var outline = new Path { Data = geometry, Width = width, Height = height, Stroke = ThemeBrush(ActiveTheme.LineColor),
            StrokeThickness = ActiveTheme.LineWidth, Fill = name == "Connector" ? null : ThemeBrush(ActiveTheme.EntityFill),
            Cursor = Cursors.SizeAll, ToolTip = name };
        var shape = new Grid { Tag = "DrawingShape", Width = width, Height = height, Cursor = Cursors.SizeAll };
        shape.Children.Add(outline);
        var editor = new TextBox
        {
            Text = "Drawing", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Brushes.White, CaretBrush = Brushes.White,
            FontFamily = new FontFamily(ActiveTheme.DefaultFont + ", Segoe UI"), FontSize = 12,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(32, 28, 32, 28),
            Padding = new Thickness(2), Cursor = Cursors.IBeam,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        System.Windows.Automation.AutomationProperties.SetName(editor, name + " text");
        editor.PreviewMouseLeftButtonDown += (_, _) => SelectDrawing(shape);
        editor.GotKeyboardFocus += (_, _) => { SelectDrawing(shape); };
        shape.Children.Add(editor);
        void RemoveDrawing()
        {
            if (_selectedDrawing == shape) ClearDrawingSelection();
            if (_connectorSource == shape) _connectorSource = null;
            DiagramCanvasSurface.Children.Remove(shape);
            UpdateDrawingConnectors(); RefreshEndpointHandles();
        }
        var close = new Button
        {
            Content = "×", Width = 22, Height = 22, Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, 0), Foreground = ThemeBrush("#A9C9E8"),
            Background = Brushes.Transparent, ToolTip = "Delete drawing", Cursor = Cursors.Hand
        };
        close.Click += (_, args) => { RemoveDrawing(); args.Handled = true; };
        shape.Children.Add(close);
        var resize = new Thumb { Style = (Style)FindResource("CardResizeThumb"), ToolTip = "Resize drawing" };
        Point resizeStart = default;
        double initialWidth = width, initialHeight = height;
        resize.DragStarted += (_, args) =>
        {
            SelectDrawing(shape);
            resizeStart = Mouse.GetPosition(DiagramCanvasSurface);
            initialWidth = shape.Width; initialHeight = shape.Height;
            args.Handled = true;
        };
        resize.DragDelta += (_, args) =>
        {
            var delta = Mouse.GetPosition(DiagramCanvasSurface) - resizeStart;
            shape.Width = Math.Max(100, initialWidth + delta.X);
            shape.Height = Math.Max(80, initialHeight + delta.Y);
            outline.Width = shape.Width; outline.Height = shape.Height;
            var resized = geometry.Clone();
            resized.Transform = new ScaleTransform(shape.Width / width, shape.Height / height);
            outline.Data = resized;
            editor.Margin = new Thickness(shape.Width * 32 / width, shape.Height * 28 / height,
                shape.Width * 32 / width, shape.Height * 28 / height);
            UpdateDrawingConnectors(); RefreshEndpointHandles();
            StatusText.Text = $"Resizing {name}: {shape.Width:0} × {shape.Height:0}";
            args.Handled = true;
        };
        resize.DragCompleted += (_, args) => { StatusText.Text = $"Resized {name}"; args.Handled = true; };
        shape.Children.Add(resize);
        DiagramCanvasSurface.Children.Add(shape); Panel.SetZIndex(shape, 1);
        var scale = _zoom / 100d;
        Canvas.SetLeft(shape, Math.Max(8, (DiagramScrollViewer.HorizontalOffset + DiagramScrollViewer.ViewportWidth / 2) / scale - width / 2));
        Canvas.SetTop(shape, Math.Max(8, (DiagramScrollViewer.VerticalOffset + DiagramScrollViewer.ViewportHeight / 2) / scale - height / 2));
        Point start = default; double left = 0, top = 0;
        shape.MouseLeftButtonDown += (_, args) => { if (args.OriginalSource is DependencyObject source && (source == editor || editor.IsAncestorOf(source) || source == close || close.IsAncestorOf(source) || source == resize || resize.IsAncestorOf(source))) return; SelectDrawing(shape); StatusText.Text = $"Selected {name}"; start = args.GetPosition(DiagramCanvasSurface); left = Canvas.GetLeft(shape); top = Canvas.GetTop(shape); shape.CaptureMouse(); args.Handled = true; };
        shape.MouseMove += (_, args) =>
        {
            if (!shape.IsMouseCaptured || args.LeftButton != MouseButtonState.Pressed) return;
            var delta = args.GetPosition(DiagramCanvasSurface) - start;
            Canvas.SetLeft(shape, Math.Max(8, left + delta.X)); Canvas.SetTop(shape, Math.Max(8, top + delta.Y));
        };
        shape.MouseLeftButtonUp += (_, args) => { shape.ReleaseMouseCapture(); args.Handled = true; };
        var menu = new ContextMenu(); var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, _) => RemoveDrawing(); menu.Items.Add(delete); shape.ContextMenu = menu;
        editor.Focus(); editor.SelectAll();
        StatusText.Text = $"Added {name} — type text, or drag the outer shape to move";
    }
}
