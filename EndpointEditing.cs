using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private sealed class EndpointPositions
    {
        public Point? Source;
        public Point? Target;
        public Rect SourceBounds, TargetBounds;
        public Point Start, End;
    }
    private readonly ConditionalWeakTable<Path, EndpointPositions> _endpointPositions = new();
    private Thumb? _sourceEndpointHandle, _targetEndpointHandle;
    private Path? _endpointHandleLine;

    private static Rect EndpointBounds(FrameworkElement element)
    {
        var path = element is Grid { Tag: "DrawingShape" } grid ? grid.Children.OfType<Path>().FirstOrDefault() : element as Path;
        var bounds = path?.Data.Bounds ?? new Rect(0, 0, element.ActualWidth, element.ActualHeight);
        bounds.Offset(Canvas.GetLeft(element), Canvas.GetTop(element)); return bounds;
    }
    private Point ResolveEndpoint(Path line, bool source, Rect bounds, Point fallback)
    {
        var state = _endpointPositions.GetOrCreateValue(line);
        if (source) state.SourceBounds = bounds; else state.TargetBounds = bounds;
        var normalized = source ? state.Source : state.Target;
        var point = normalized is { } p ? new Point(bounds.Left + p.X * bounds.Width, bounds.Top + p.Y * bounds.Height) : fallback;
        if (source) state.Start = point; else state.End = point;
        return point;
    }
    private static Vector EndpointNormal(Point point, Rect bounds)
    {
        var distances = new[] { Math.Abs(point.X - bounds.Left), Math.Abs(point.X - bounds.Right), Math.Abs(point.Y - bounds.Top), Math.Abs(point.Y - bounds.Bottom) };
        var side = Array.IndexOf(distances, distances.Min());
        return side switch { 0 => new Vector(-1, 0), 1 => new Vector(1, 0), 2 => new Vector(0, -1), _ => new Vector(0, 1) };
    }
    private void RefreshEndpointHandles()
    {
        var line = _selectedDrawingConnector ?? _selectedRelationship;
        if (line is null || !DiagramCanvasSurface.Children.Contains(line) || line.Visibility != Visibility.Visible || !_endpointPositions.TryGetValue(line, out var state))
        {
            if (_sourceEndpointHandle is not null) _sourceEndpointHandle.Visibility = Visibility.Collapsed;
            if (_targetEndpointHandle is not null) _targetEndpointHandle.Visibility = Visibility.Collapsed;
            _endpointHandleLine = null; return;
        }
        _endpointHandleLine = line;
        _sourceEndpointHandle ??= CreateEndpointHandle(true);
        _targetEndpointHandle ??= CreateEndpointHandle(false);
        Place(_sourceEndpointHandle, state.Start); Place(_targetEndpointHandle, state.End);
        void Place(Thumb handle, Point point)
        {
            handle.Visibility = Visibility.Visible;
            if (Canvas.GetLeft(handle) != point.X - 6) Canvas.SetLeft(handle, point.X - 6);
            if (Canvas.GetTop(handle) != point.Y - 6) Canvas.SetTop(handle, point.Y - 6);
        }
    }
    private Thumb CreateEndpointHandle(bool source)
    {
        var handle = new Thumb { Width = 12, Height = 12, Cursor = Cursors.Cross, ToolTip = source ? "Drag source endpoint" : "Drag target endpoint" };
        handle.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Ellipse Fill='#F1BF52' Stroke='#FFF1A8' StrokeThickness='1.5'/></ControlTemplate>");
        Panel.SetZIndex(handle, 40); DiagramCanvasSurface.Children.Add(handle);
        handle.DragDelta += (_, args) =>
        {
            if (_endpointHandleLine is not { } line || !_endpointPositions.TryGetValue(line, out var state)) return;
            var bounds = source ? state.SourceBounds : state.TargetBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var mouse = Mouse.GetPosition(DiagramCanvasSurface);
            var point = new Point(Math.Clamp(mouse.X, bounds.Left, bounds.Right), Math.Clamp(mouse.Y, bounds.Top, bounds.Bottom));
            var normal = EndpointNormal(point, bounds);
            if (normal.X != 0) point.X = normal.X < 0 ? bounds.Left : bounds.Right;
            else point.Y = normal.Y < 0 ? bounds.Top : bounds.Bottom;
            var normalized = new Point((point.X - bounds.Left) / bounds.Width, (point.Y - bounds.Top) / bounds.Height);
            if (source) state.Source = normalized; else state.Target = normalized;
            if (line.Tag is DrawingConnection connection) { connection.LastGeometry = ""; UpdateDrawingConnectors(); }
            else UpdateRelationshipLines();
            RefreshEndpointHandles(); args.Handled = true;
        };
        return handle;
    }
}
