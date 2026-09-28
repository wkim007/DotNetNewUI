using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private FrameworkElement? _selectedDrawing;
    private FrameworkElement? _connectorSource;
    private sealed class DrawingConnection(FrameworkElement source, FrameworkElement target)
    {
        public FrameworkElement Source { get; } = source;
        public FrameworkElement Target { get; } = target;
        public string LastGeometry { get; set; } = "";
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
            !(element is Border { Tag: string } || element is Path { Tag: "DrawingShape" })) return;
        e.Handled = true;
        if (element == _connectorSource) { StatusText.Text = "Choose a different target object"; return; }
        if (!DiagramCanvasSurface.Children.Contains(_connectorSource)) { _connectorSource = null; return; }
        var line = new Path
        {
            Tag = new DrawingConnection(_connectorSource, element), Stroke = ThemeBrush("#8FAFFF"),
            StrokeThickness = 1.8, Cursor = Cursors.Hand, ToolTip = "Drawing connector"
        };
        var menu = new ContextMenu(); var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, _) => DiagramCanvasSurface.Children.Remove(line); menu.Items.Add(delete); line.ContextMenu = menu;
        Panel.SetZIndex(line, 0); DiagramCanvasSurface.Children.Add(line);
        _connectorSource = null; UpdateDrawingConnectors(); StatusText.Text = "Connector created";
    }

    private void UpdateDrawingConnectors()
    {
        foreach (var line in DiagramCanvasSurface.Children.OfType<Path>().Where(p => p.Tag is DrawingConnection).ToArray())
        {
            var link = (DrawingConnection)line.Tag;
            if (!DiagramCanvasSurface.Children.Contains(link.Source) || !DiagramCanvasSurface.Children.Contains(link.Target))
            { DiagramCanvasSurface.Children.Remove(line); continue; }
            Rect Bounds(FrameworkElement item)
            {
                var local = item is Path shape ? shape.Data.Bounds : new Rect(0, 0, item.ActualWidth, item.ActualHeight);
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
            var figure = new PathFigure { StartPoint = start, IsFilled = false };
            if (_relationshipLineStyle == "curve")
            {
                var distance = Math.Max(24, (end - start).Length / 2);
                var tangent = horizontal ? new Vector(sign * distance, 0) : new Vector(0, sign * distance);
                figure.Segments.Add(new BezierSegment(start + tangent, end - tangent, end, true));
            }
            else figure.Segments.Add(new LineSegment(end, true));
            line.Data = new PathGeometry([figure]);
        }
    }
}
