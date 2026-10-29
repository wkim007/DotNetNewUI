using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private Path? _selectedSubCategoryMarker;

    private void ConfigureSubCategoryMarker(Path marker, string sourceKey)
    {
        marker.IsHitTestVisible = true;
        Panel.SetZIndex(marker, 7);
        if (marker.ContextMenu is not null) return;
        marker.ContextMenu = CreateTextDeleteMenu(out var delete);
        var rollUp = new MenuItem { Header = "RollUp", Template = delete.Template, Padding = delete.Padding };
        var rollDown = new MenuItem { Header = "RollDown", Template = delete.Template, Padding = delete.Padding };
        marker.ContextMenu.Items.Insert(0, rollUp);
        marker.ContextMenu.Items.Insert(1, rollDown);
        var identify = new MenuItem { Header = "Supertype-Subtype Identify", Template = delete.Template, Padding = delete.Padding };
        marker.ContextMenu.Items.Insert(2, identify);
        AddPhysicalRollMenus(marker.ContextMenu, delete, sourceKey);
        identify.Click += (_, _) => IdentifySubCategory(sourceKey);
        rollUp.Click += (_, _) => RollSubCategory(sourceKey, true);
        rollDown.Click += (_, _) => RollSubCategory(sourceKey, false);
        void SelectMarker()
        {
            CancelPendingRelationshipTool();
            ClearAttributeSelection(); ClearCardSelection(); ClearRelationshipSelection();
            _selectedSubCategoryMarker = marker;
            marker.Stroke = ThemeBrush("#FFD45A");
            marker.StrokeThickness = 3;
            StatusText.Text = $"Sub-Category of '{EntityDisplayName(sourceKey)}' selected.";
        }
        marker.MouseLeftButtonDown += (_, e) => { SelectMarker(); e.Handled = true; };
        marker.MouseRightButtonDown += (_, e) => { SelectMarker(); e.Handled = true; };
        delete.Click += (_, _) =>
        {
            var keys = _dynamicRelationships.Where(p => p.Value.IsSubCategory && p.Value.SourceKey == sourceKey &&
                DiagramCanvasSurface.Children.Contains(p.Value.Visible)).Select(p => p.Key).ToArray();
            ClearRelationshipSelection();
            foreach (var key in keys) DeleteRelationship(key);
            SaveActiveDiagram();
            StatusText.Text = $"Deleted {keys.Length} Sub-Category relationships from '{EntityDisplayName(sourceKey)}'.";
        };
    }

    private void UpdateSubCategoryRelationship(string key, Border parent, Border child, Path line, Path hit, TextBlock label)
    {
        var relation = _dynamicRelationships[key];
        ConfigureIdentifiedBranchMenu(key, hit, relation);
        var siblings = _dynamicRelationships.Where(p => p.Value.IsSubCategory &&
            p.Value.SourceKey == relation.SourceKey && !_deletedRelationships.Contains(p.Key) &&
            FindCard(p.Value.TargetKey) is { } card && DiagramCanvasSurface.Children.Contains(card)).ToArray();
        var parentBounds = EndpointBounds(parent);
        var childBounds = EndpointBounds(child);
        Point Center(Rect r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);
        Point Face(Rect r, Point toward)
        {
            var center = Center(r);
            var delta = toward - center;
            return Math.Abs(delta.Y) >= Math.Abs(delta.X)
                ? new Point(center.X, delta.Y >= 0 ? r.Bottom : r.Top)
                : new Point(delta.X >= 0 ? r.Right : r.Left, center.Y);
        }
        var centers = siblings.Select(p => Center(EndpointBounds(FindCard(p.Value.TargetKey)!))).ToArray();
        var destination = new Point(centers.Average(p => p.X), centers.Average(p => p.Y));
        var parentEdge = Face(parentBounds, destination);
        var direction = EndpointNormal(parentEdge, parentBounds);
        if (ViewModeBox.SelectedIndex != 1 || relation.IsSupertypeSubtypeIdentified)
        {
            // Physical tables have independent identifying connections, with
            // separate parent anchors instead of the logical shared junction.
            var index = Array.FindIndex(siblings, p => p.Key == key);
            var fraction = (index + 1d) / (siblings.Length + 1d);
            var physicalStart = direction.Y != 0
                ? new Point(parentBounds.Left + parentBounds.Width * fraction, parentEdge.Y)
                : new Point(parentEdge.X, parentBounds.Top + parentBounds.Height * fraction);
            physicalStart = ResolveEndpoint(line, true, parentBounds, physicalStart);
            var physicalEnd = ResolveEndpoint(line, false, childBounds, Face(childBounds, physicalStart));
            var fromStub = physicalStart + EndpointNormal(physicalStart, parentBounds) * 24;
            var toStub = physicalEnd + EndpointNormal(physicalEnd, childBounds) * 24;
            _relationshipOffsets.TryGetValue(key, out var physicalOffset);
            var bend = direction.Y != 0 ? (fromStub.Y + toStub.Y) / 2 + physicalOffset.Y
                : (fromStub.X + toStub.X) / 2 + physicalOffset.X;
            var points = direction.Y != 0
                ? new[] { physicalStart, fromStub, new Point(fromStub.X, bend), new Point(toStub.X, bend), toStub, physicalEnd }
                : new[] { physicalStart, fromStub, new Point(bend, fromStub.Y), new Point(bend, toStub.Y), toStub, physicalEnd };
            var physicalFigure = new PathFigure { StartPoint = points[0], IsFilled = false };
            foreach (var point in points.Skip(1)) physicalFigure.Segments.Add(new LineSegment(point, true));
            line.StrokeDashArray = null;
            line.Data = new PathGeometry([physicalFigure]);
            hit.Data = line.Data.Clone();
            label.Visibility = Visibility.Collapsed;
            UpdateNotationEndpoints(key, line, points.ToList(), "1:1");
            var physicalMarker = _notationEndpoints[key].Marker;
            if (_selectedSubCategoryMarker == physicalMarker) ClearRelationshipSelection();
            physicalMarker.Data = Geometry.Empty;
            physicalMarker.IsHitTestVisible = false;
            if (relation.IsSupertypeSubtypeIdentified)
            {
                var parentNormal = EndpointNormal(physicalStart, parentBounds);
                var childNormal = EndpointNormal(physicalEnd, childBounds);
                LineGeometry Bar(Point endpoint, Vector normal)
                {
                    var center = endpoint + normal * 8;
                    var tangent = new Vector(-normal.Y, normal.X) * 6;
                    return new LineGeometry(center - tangent, center + tangent);
                }
                physicalMarker.Data = ViewModeBox.SelectedIndex != 1
                    ? new EllipseGeometry(physicalEnd + childNormal * 5, 4.5, 4.5)
                    : new GeometryGroup { Children = {
                    Bar(physicalStart, parentNormal), Bar(physicalEnd, childNormal),
                    new EllipseGeometry(physicalEnd + childNormal * 17, 5, 5)
                }};
                physicalMarker.SetBinding(Shape.StrokeProperty, new Binding(nameof(Shape.Stroke)) { Source = line });
                physicalMarker.StrokeThickness = 1.5;
                physicalMarker.Fill = ViewModeBox.SelectedIndex == 1 ? ThemeBrush(ActiveTheme.DiagramFill) : ThemeBrush("#91A3B2");
                physicalMarker.IsHitTestVisible = ViewModeBox.SelectedIndex == 1;
            }
            if (_selectedRelationshipKey == key)
            {
                Canvas.SetLeft(RelationshipMoveHandle, points[2].X - 9); Canvas.SetTop(RelationshipMoveHandle, points[2].Y - 9);
                Canvas.SetLeft(RelationshipDeleteButton, points[2].X - 13); Canvas.SetTop(RelationshipDeleteButton, points[2].Y - 47);
            }
            return;
        }
        var junction = parentEdge + direction * 60;
        var apex = junction - direction * 14;
        var start = ResolveEndpoint(line, true, parentBounds, parentEdge);
        var end = ResolveEndpoint(line, false, childBounds, Face(childBounds, junction));
        var startStub = start + EndpointNormal(start, parentBounds) * 24;
        var endStub = end + EndpointNormal(end, childBounds) * 24;
        _relationshipOffsets.TryGetValue(key, out var offset);
        var trunk = direction.Y != 0 ? junction.Y + offset.Y : junction.X + offset.X;
        var branch = direction.Y != 0
            ? new[] { junction, new Point(junction.X, trunk), new Point(endStub.X, trunk), endStub, end }
            : new[] { junction, new Point(trunk, junction.Y), new Point(trunk, endStub.Y), endStub, end };
        var stem = direction.Y != 0
            ? new[] { start, startStub, new Point(apex.X, startStub.Y), apex }
            : new[] { start, startStub, new Point(startStub.X, apex.Y), apex };
        PathFigure Figure(Point[] points)
        {
            var figure = new PathFigure { StartPoint = points[0], IsFilled = false, IsClosed = false };
            foreach (var point in points.Skip(1)) figure.Segments.Add(new LineSegment(point, true));
            return figure;
        }
        line.StrokeDashArray = null;
        line.Data = new PathGeometry([Figure(stem), Figure(branch)]);
        hit.Data = line.Data.Clone();
        label.Text = "1:1";
        label.Visibility = Visibility.Collapsed;
        UpdateNotationEndpoints(key, line, [start, startStub, endStub, end], "1:1");
        var marker = _notationEndpoints[key].Marker;
        ConfigureSubCategoryMarker(marker, relation.SourceKey);
        marker.SetBinding(Shape.StrokeProperty, new Binding(nameof(Shape.Stroke)) { Source = line });
        marker.StrokeThickness = 1.5;
        if (siblings[0].Key == key)
        {
            var tangent = new Vector(direction.Y, -direction.X);
            var left = junction - tangent * 14;
            var right = junction + tangent * 14;
            var dome = new PathFigure { StartPoint = left, IsClosed = true, IsFilled = true };
            dome.Segments.Add(new ArcSegment(right, new Size(14, 14), 0, false, SweepDirection.Clockwise, true));
            var outline = new PathGeometry([dome]);
            marker.Data = new GeometryGroup { Children = {
                outline, new LineGeometry(left, junction - direction * 10), new LineGeometry(junction - direction * 10, right)
            }};
        }
        else marker.Data = Geometry.Empty;
        if (_selectedSubCategoryMarker == marker)
        {
            marker.Stroke = ThemeBrush("#FFD45A");
            marker.StrokeThickness = 3;
        }
        if (_selectedRelationshipKey == key)
        {
            var middle = branch[2];
            RelationshipMoveHandle.Visibility = RelationshipDeleteButton.Visibility = Visibility.Visible;
            Canvas.SetLeft(RelationshipMoveHandle, middle.X - 9); Canvas.SetTop(RelationshipMoveHandle, middle.Y - 9);
            RelationshipDeleteButton.Tag = key;
            Canvas.SetLeft(RelationshipDeleteButton, middle.X - 13); Canvas.SetTop(RelationshipDeleteButton, middle.Y - 47);
        }
    }

    private void IdentifySubCategory(string sourceKey)
    {
        var group = _dynamicRelationships.Where(p => p.Value.IsSubCategory && p.Value.SourceKey == sourceKey &&
            DiagramCanvasSurface.Children.Contains(p.Value.Visible)).ToArray();
        if (group.Length == 0) return;
        ClearRelationshipSelection();
        foreach (var pair in group)
            _dynamicRelationships[pair.Key] = pair.Value with { IsSupertypeSubtypeIdentified = true };
        UpdateRelationshipLines();
        SaveActiveDiagram();
        StatusText.Text = "Supertype-Subtype Identify completed: individual parent and child connections created.";
    }

    private void ConfigureIdentifiedBranchMenu(string key, Path hit, DynamicRelationship relation)
    {
        if (ViewModeBox.SelectedIndex != 1 || !relation.IsSupertypeSubtypeIdentified)
        { hit.ContextMenu = null; return; }
        if (hit.ContextMenu is not null) return;
        var menu = CreateTextDeleteMenu(out var up);
        up.Header = "RollUp";
        var down = new MenuItem { Header = "RollDown", Template = up.Template, Padding = up.Padding };
        menu.Items.Add(down);
        AddPhysicalRollMenus(menu, up, relation.SourceKey, key);
        up.Click += (_, _) => RollIdentifiedBranch(key, true);
        down.Click += (_, _) => RollIdentifiedBranch(key, false);
        menu.Opened += (_, _) =>
        {
            if (ViewModeBox.SelectedIndex != 1) { menu.IsOpen = false; return; }
            ClearAttributeSelection(); ClearCardSelection(); ClearRelationshipSelection();
            _selectedHitPath = hit; _selectedRelationshipKey = key;
            _selectedRelationship = FindRelationshipLine(key);
            if (_selectedRelationship is { } line) { line.Stroke = ThemeBrush("#F1DF77"); line.StrokeThickness = 4; }
            ShowRelationshipProperties(key);
        };
        hit.ContextMenu = menu;
    }

    private void OpenIdentifiedBranchMenu(string? key, FrameworkElement target, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ViewModeBox.SelectedIndex != 1 || key is null ||
            !_dynamicRelationships.TryGetValue(key, out var relation) || !relation.IsSupertypeSubtypeIdentified ||
            relation.Hit.ContextMenu is not { } menu) return;
        menu.PlacementTarget = target;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }
}
