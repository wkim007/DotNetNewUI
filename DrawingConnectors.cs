using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private Path? _selectedDrawingConnector;
    private FrameworkElement? _selectedDrawing;
    private Path? _selectedDrawingOutline;
    private Brush? _drawingOriginalStroke;
    private double _drawingOriginalWidth;

    private void SelectDrawing(FrameworkElement drawing)
    {
        if (_selectedDrawing == drawing) return;
        ClearAttributeSelection();
        ClearCardSelection();
        ClearRelationshipSelection();
        _selectedDrawing = drawing;
        _selectedDrawingOutline = drawing is Grid grid ? grid.Children.OfType<Path>().FirstOrDefault() : drawing as Path;
        if (_selectedDrawingOutline is { } outline)
        {
            _drawingOriginalStroke = outline.Stroke;
            _drawingOriginalWidth = outline.StrokeThickness;
            outline.Stroke = new SolidColorBrush(Color.FromRgb(241, 191, 82));
            outline.StrokeThickness = Math.Max(3, _drawingOriginalWidth + 1);
        }
        Panel.SetZIndex(drawing, 10);
    }

    private void ClearDrawingSelection()
    {
        ClearDrawingConnectorSelection();
        if (_selectedDrawingOutline is { } outline)
        {
            outline.Stroke = _drawingOriginalStroke;
            outline.StrokeThickness = _drawingOriginalWidth;
        }
        if (_selectedDrawing is { } drawing) Panel.SetZIndex(drawing, 1);
        _selectedDrawing = null;
        _selectedDrawingOutline = null;
        _drawingOriginalStroke = null;
    }
    private FrameworkElement? _connectorSource;
    private sealed class DrawingConnection(FrameworkElement source, FrameworkElement target)
    {
        public FrameworkElement Source { get; } = source;
        public FrameworkElement Target { get; } = target;
        public string LastGeometry { get; set; } = "";
    }

    private void SelectDrawingConnector(Path line)
    {
        ClearAttributeSelection(); ClearCardSelection(); ClearRelationshipSelection();
        _connectorSource = null;
        _selectedDrawingConnector = line;
        line.Stroke = ThemeBrush("#F1BF52");
        line.StrokeThickness = 2.5;
        line.Fill = ThemeBrush("#F1BF52");
        ((DrawingConnection)line.Tag).LastGeometry = "";
        UpdateDrawingConnectors();
        StatusText.Text = "Drawing connector selected";
    }

    private void ClearDrawingConnectorSelection()
    {
        if (_selectedDrawingConnector is not { } line) return;
        _selectedDrawingConnector = null;
        line.Stroke = ThemeBrush("#8FAFFF"); line.StrokeThickness = 1.8; line.Fill = null;
        if (line.Tag is DrawingConnection link) link.LastGeometry = "";
        UpdateDrawingConnectors();
    }
    private void BeginDrawingConnector()
    {
        var source = _selectedDrawing ?? (FrameworkElement?)_selectedCard;
        if (source is null || !DiagramCanvasSurface.Children.Contains(source))
        {
            StatusText.Text = "Select a source object or drawing first, then choose Connector";
            return;
        }
        _pendingRelationshipType = null; _relationshipSourceKey = null;
        _connectorSource = source;
        StatusText.Text = "Connector: click the target object or drawing (Escape to cancel)";
    }

    private void DrawingConnectorTarget_Click(object sender, MouseButtonEventArgs e)
    {
        if (_connectorSource is null) return;
        var target = e.OriginalSource as DependencyObject;
        while (target is not null && VisualTreeHelper.GetParent(target) != DiagramCanvasSurface)
            target = VisualTreeHelper.GetParent(target);
        if (target is not FrameworkElement element ||
            !(element is Border { Tag: string } || element is FrameworkElement { Tag: "DrawingShape" })) return;
        e.Handled = true;
        if (element == _connectorSource) { StatusText.Text = "Choose a different target object"; return; }
        if (!DiagramCanvasSurface.Children.Contains(_connectorSource)) { _connectorSource = null; return; }
        var line = new Path
        {
            Tag = new DrawingConnection(_connectorSource, element), Stroke = ThemeBrush("#8FAFFF"),
            StrokeThickness = 1.8, Cursor = Cursors.Hand, ToolTip = "Drawing connector"
        };
        line.MouseLeftButtonDown += (_, args) => { SelectDrawingConnector(line); args.Handled = true; };
        var menu = new ContextMenu(); var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, _) => { if (_selectedDrawingConnector == line) ClearDrawingConnectorSelection(); DiagramCanvasSurface.Children.Remove(line); }; menu.Items.Add(delete); line.ContextMenu = menu;
        Panel.SetZIndex(line, 0); DiagramCanvasSurface.Children.Add(line);
        _connectorSource = null; UpdateDrawingConnectors(); StatusText.Text = "Connector created";
    }

    private static Point ProjectDrawingEndpoint(FrameworkElement element, Point desired)
    {
        var outline = element is Grid { Tag: "DrawingShape" } grid
            ? grid.Children.OfType<Path>().FirstOrDefault() : element as Path;
        if (outline?.Data is null) return desired;
        // Project to the rendered contour, not its bounding rectangle. Flattening also
        // supports ellipses and rounded rectangles with a subpixel approximation.
        var flattened = outline.Data.GetFlattenedPathGeometry(0.1, ToleranceType.Absolute);
        var offset = new Vector(Canvas.GetLeft(element), Canvas.GetTop(element));
        var transform = flattened.Transform ?? Transform.Identity;
        Point ToCanvas(Point point) => transform.Transform(point) + offset;
        var closest = desired;
        var shortest = double.PositiveInfinity;
        void Consider(Point a, Point b)
        {
            var edge = b - a;
            var t = edge.LengthSquared < 0.000001 ? 0 : Math.Clamp(Vector.Multiply(desired - a, edge) / edge.LengthSquared, 0, 1);
            var candidate = a + edge * t;
            var distance = (candidate - desired).LengthSquared;
            if (distance < shortest) { shortest = distance; closest = candidate; }
        }
        foreach (var figure in flattened.Figures)
        {
            var first = ToCanvas(figure.StartPoint);
            var previous = first;
            foreach (var segment in figure.Segments)
            {
                if (segment is PolyLineSegment polyline)
                    foreach (var point in polyline.Points)
                    {
                        var next = ToCanvas(point); Consider(previous, next); previous = next;
                    }
                else if (segment is LineSegment line)
                {
                    var next = ToCanvas(line.Point); Consider(previous, next); previous = next;
                }
            }
            if (figure.IsClosed) Consider(previous, first);
        }
        return closest;
    }
    private void UpdateDrawingConnectors()
    {
        foreach (var line in DiagramCanvasSurface.Children.OfType<Path>().Where(p => p.Tag is DrawingConnection).ToArray())
        {
            var link = (DrawingConnection)line.Tag;
            if (!DiagramCanvasSurface.Children.Contains(link.Source) || !DiagramCanvasSurface.Children.Contains(link.Target))
            { if (_selectedDrawingConnector == line) _selectedDrawingConnector = null; DiagramCanvasSurface.Children.Remove(line); continue; }
            Rect Bounds(FrameworkElement item)
            {
                var drawingPath = item is Grid { Tag: "DrawingShape" } drawing ? drawing.Children.OfType<Path>().FirstOrDefault() : item as Path;
                var local = drawingPath is not null ? drawingPath.Data.Bounds : new Rect(0, 0, item.ActualWidth, item.ActualHeight);
                local.Offset(Canvas.GetLeft(item), Canvas.GetTop(item)); return local;
            }
            var source = Bounds(link.Source); var target = Bounds(link.Target);
            var signature = $"{source}|{target}|{_relationshipLineStyle}";
            if (link.LastGeometry == signature) continue;
            link.LastGeometry = signature;
            var a = new Point(source.X + source.Width / 2, source.Y + source.Height / 2);
            var b = new Point(target.X + target.Width / 2, target.Y + target.Height / 2);
            var horizontal = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
            var sign = horizontal ? (b.X >= a.X ? 1 : -1) : (b.Y >= a.Y ? 1 : -1);
            var start = horizontal ? new Point(a.X + sign * source.Width / 2, a.Y) : new Point(a.X, a.Y + sign * source.Height / 2);
            var end = horizontal ? new Point(b.X - sign * target.Width / 2, b.Y) : new Point(b.X, b.Y - sign * target.Height / 2);
            start = ResolveEndpoint(line, true, source, start);
            end = ResolveEndpoint(line, false, target, end);
            var startNormal = EndpointNormal(start, source);
            var endNormal = EndpointNormal(end, target);
            start = ProjectDrawingEndpoint(link.Source, start);
            end = ProjectDrawingEndpoint(link.Target, end);
            var positions = _endpointPositions.GetOrCreateValue(line);
            positions.Start = start;
            positions.End = end;
            var figure = new PathFigure { StartPoint = start, IsFilled = false };
            if (_relationshipLineStyle == "curve")
            {
                var distance = Math.Max(24, (end - start).Length / 2);
                var tangent = horizontal ? new Vector(sign * distance, 0) : new Vector(0, sign * distance);
                figure.Segments.Add(new BezierSegment(start + startNormal * distance, end + endNormal * distance, end, true));
            }
            else figure.Segments.Add(new LineSegment(end, true));
            if (line == _selectedDrawingConnector)
            {
                var geometry = new GeometryGroup();
                geometry.Children.Add(new PathGeometry([figure]));
                // Offset dots slightly outside the objects so card borders cannot cover them.
                var outward = horizontal ? new Vector(sign * 4, 0) : new Vector(0, sign * 4);
                geometry.Children.Add(new EllipseGeometry(start + outward, 3, 3));
                geometry.Children.Add(new EllipseGeometry(end - outward, 3, 3));
                line.Data = geometry;
            }
            else line.Data = new PathGeometry([figure]);
        }
    }
}
