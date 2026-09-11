using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private readonly Dictionary<string, (Path Line, Path Marker)> _notationEndpoints = new();

    private void UpdateNotationEndpoints(string key, Path line, List<Point> route, string cardinality)
    {
        if (!_notationEndpoints.TryGetValue(key, out var pair))
        {
            var marker = new Path { IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round };
            marker.SetBinding(Shape.StrokeProperty, new Binding(nameof(Shape.Stroke)) { Source = line });
            marker.SetBinding(Shape.StrokeThicknessProperty, new Binding(nameof(Shape.StrokeThickness)) { Source = line });
            Panel.SetZIndex(marker, 2);
            DiagramCanvasSurface.Children.Add(marker);
            pair = (line, marker);
            _notationEndpoints.Add(key, pair);
        }
        var geometry = new GeometryGroup();
        void Segment(Point start, Point end)
        {
            geometry.Children.Add(new LineGeometry(start, end));
        }
        void Endpoint(Point endpoint, Point outside, bool many)
        {
            var direction = outside - endpoint;
            if (direction.Length < 0.001) return;
            direction.Normalize();
            var normal = new Vector(-direction.Y, direction.X);
            // Solid terminal stems join the glyph even when the relationship is dashed.
            Segment(endpoint, outside);
            if (!many)
            {
                var center = endpoint + direction * 9;
                Segment(center - normal * 6, center + normal * 6);
                return;
            }
            // The fork opens toward its entity; the optionality circle sits outside it.
            var fork = endpoint + direction * 12;
            Segment(fork, endpoint + normal * 6);
            Segment(fork, endpoint - normal * 6);
            Segment(fork, endpoint);
            geometry.Children.Add(new EllipseGeometry(endpoint + direction * 20, 4, 4));
        }
        var ends = cardinality.Split(':');
        var notation = ViewModeBox.SelectedIndex == 1 ? _logicalNotation : _physicalNotation;
        if (notation == "Information Engineering")
        {
            Endpoint(route[0], route[1], ends.Length == 2 && ends[0] == "N");
            Endpoint(route[^1], route[^2], ends.Length == 2 && ends[1] == "N");
            pair.Marker.SetBinding(Shape.StrokeProperty, new Binding(nameof(Shape.Stroke)) { Source = line });
            pair.Marker.SetBinding(Shape.StrokeThicknessProperty, new Binding(nameof(Shape.StrokeThickness)) { Source = line });
        }
        else
        {
            var direction = route[^2] - route[^1];
            if (direction.Length > 0) direction.Normalize();
            geometry.Children.Add(new EllipseGeometry(route[^1] + direction * 5, 4.5, 4.5));
            BindingOperations.ClearBinding(pair.Marker, Shape.StrokeProperty);
            BindingOperations.ClearBinding(pair.Marker, Shape.StrokeThicknessProperty);
            pair.Marker.Stroke = new SolidColorBrush(Color.FromRgb(218, 231, 242));
            pair.Marker.StrokeThickness = 1.2;
        }
        pair.Marker.Data = geometry;
        SyncNotationEndpoints();
    }

    private void SyncNotationEndpoints()
    {
        var notation = ViewModeBox.SelectedIndex == 1 ? _logicalNotation : _physicalNotation;
        var enabled = notation == "Information Engineering" || notation == "IDEF1x" || string.IsNullOrEmpty(notation);
        foreach (var (key, pair) in _notationEndpoints.ToArray())
        {
            if (!DiagramCanvasSurface.Children.Contains(pair.Line))
            {
                DiagramCanvasSurface.Children.Remove(pair.Marker);
                _notationEndpoints.Remove(key);
                continue;
            }
            var visibility = enabled && pair.Line.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
            if (pair.Marker.Visibility != visibility) pair.Marker.Visibility = visibility;
            var color = notation == "Information Engineering"
                ? (Color)ColorConverter.ConvertFromString(ActiveTheme.DiagramFill)
                : Color.FromRgb(145, 163, 178);
            if (pair.Marker.Fill is not SolidColorBrush brush || brush.Color != color)
                pair.Marker.Fill = new SolidColorBrush(color);
        }
    }
}
